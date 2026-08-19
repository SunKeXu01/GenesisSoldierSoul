#!/usr/bin/env python3
"""Grade formal Unity resources without turning provenance into a licence claim."""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import re
from collections import Counter, deque
from pathlib import Path
from typing import Any

GUID_RE = re.compile(rb"guid:\s*([0-9a-f]{32})")
LIGHTING_SCENE_BACKREF_RE = re.compile(
    rb"^\s*m_Scene:\s*\{[^}]*guid:\s*([0-9a-f]{32})",
    re.MULTILINE,
)
ZERO_GUID = "0" * 32
BUILTIN_GUIDS = {
    "0000000000000000e000000000000000",
    "0000000000000000f000000000000000",
    # Legacy UnityEngine.UI assembly reference used by pre-package scenes.
    "f5f67c52d1564df4a8936ccd202a3bd8",
}
RIGHTS_RECORD_TYPE = "formal_resource_rights_approval"
REQUIRED_RIGHTS_SCOPE = {"formal_release", "redistribution"}
RIGHTS_BASES = {
    "copyright_owner_license",
    "open_source_license",
    "original_authorship",
    "public_domain",
    "replacement_assets",
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def guid_index(project: Path) -> dict[str, str]:
    result: dict[str, str] = {}
    roots = [project / "Assets", project / "Library/PackageCache"]
    for root in roots:
        if not root.is_dir():
            continue
        for meta in root.rglob("*.meta"):
            match = re.search(r"^guid:\s*([0-9a-f]{32})\s*$", meta.read_text(
                encoding="utf-8", errors="ignore"), re.MULTILINE)
            if match:
                result[match.group(1)] = meta.relative_to(project).as_posix()[:-5]
    return result


def asset_files(root: Path, seed: str) -> list[Path]:
    path = root / seed
    if path.is_file():
        return [path]
    if path.is_dir():
        return sorted(item for item in path.rglob("*")
                      if item.is_file() and not item.name.endswith(".meta"))
    return []


def direct_guid_analysis(path: Path) -> tuple[set[str], list[dict[str, str]]]:
    try:
        data = path.read_bytes()
    except OSError:
        return set(), []
    if b"\0" in data[:4096]:
        return set(), []
    guids = {value.decode("ascii") for value in GUID_RE.findall(data)
             if value.decode("ascii") != ZERO_GUID
             and value.decode("ascii") not in BUILTIN_GUIDS}
    ignored = []
    # Unity LightingData.asset serializes m_Scene as an owner/back-reference.
    # Traversing it as a runtime dependency walks from a derived playable scene
    # back into the original source scene and produces false missing-script
    # edges. Other GUIDs in the lighting data remain ordinary dependencies.
    if path.name == "LightingData.asset":
        for value in LIGHTING_SCENE_BACKREF_RE.findall(data):
            guid = value.decode("ascii")
            if guid in guids:
                guids.remove(guid)
                ignored.append({
                    "guid": guid,
                    "reason": "Unity LightingData m_Scene owner back-reference",
                })
    return guids, ignored


def direct_guids(path: Path) -> set[str]:
    return direct_guid_analysis(path)[0]


def closure(root: Path, seeds: list[str], index: dict[str, str]) -> dict[str, Any]:
    queue = deque(asset_files(root, seed)[0:] for seed in seeds)
    pending: deque[Path] = deque()
    for items in queue:
        pending.extend(items)
    visited: set[str] = set()
    missing: set[str] = set()
    ignored_back_references: list[dict[str, str]] = []
    while pending:
        path = pending.popleft()
        relative = path.relative_to(root).as_posix()
        if relative in visited or path.name.endswith(".meta"):
            continue
        visited.add(relative)
        guids, ignored = direct_guid_analysis(path)
        for item in ignored:
            ignored_back_references.append({"path": relative, **item})
        for guid in guids:
            target = index.get(guid)
            if target is None:
                missing.add(guid)
                continue
            target_path = root / target
            if target not in visited and target_path.is_file():
                pending.append(target_path)
    return {
        "assets": sorted(visited),
        "missing_guids": sorted(missing),
        "ignored_back_references": sorted(
            ignored_back_references,
            key=lambda item: (item["path"], item["guid"]),
        ),
    }


def dimensions(paths: list[str]) -> dict[str, bool]:
    extensions = {Path(path).suffix.lower() for path in paths}
    text = "\n".join(paths).lower()
    return {
        "model_or_mesh": bool(extensions & {".fbx", ".obj", ".asset", ".prefab"}),
        "texture": bool(extensions & {".png", ".jpg", ".jpeg", ".tga", ".texture2d"}),
        "material": ".mat" in extensions,
        "skeleton_or_controller": ".controller" in extensions or "character" in text,
        "animation": ".anim" in extensions,
        "audio": bool(extensions & {".wav", ".mp3", ".ogg"}),
    }


def asset_inventory(root: Path, paths: list[str]) -> tuple[list[dict[str, Any]], str]:
    records = [
        {"path": path, "bytes": (root / path).stat().st_size, "sha256": sha256(root / path)}
        for path in paths
    ]
    canonical = json.dumps(records, ensure_ascii=False, separators=(",", ":")).encode(
        "utf-8"
    )
    return records, hashlib.sha256(canonical).hexdigest()


def repository_file(repo: Path, value: Any, label: str) -> Path:
    if not isinstance(value, str) or not value:
        raise ValueError(f"{label} must be a non-empty repository-relative path")
    relative = Path(value)
    if relative.is_absolute() or ".." in relative.parts:
        raise ValueError(f"{label} must stay inside the repository")
    target = (repo / relative).resolve()
    try:
        target.relative_to(repo.resolve())
    except ValueError as error:
        raise ValueError(f"{label} escapes the repository") from error
    return target


def validate_rights_record(
    repo: Path,
    group: dict[str, Any],
    approved: set[str],
    asset_manifest_sha256: str,
) -> dict[str, Any]:
    value = group.get("rights_record")
    if not value:
        return {"valid": False, "status": "missing", "reason": "rights record missing"}
    if not isinstance(value, str):
        return {
            "valid": False,
            "status": "invalid",
            "reason": "rights record path must be a string",
        }
    if value not in approved:
        return {
            "valid": False,
            "status": "not_policy_approved",
            "reason": "rights record is not listed in approved_rights_records",
            "path": value,
        }
    try:
        path = repository_file(repo, value, "rights record")
        if not path.is_file():
            raise ValueError("rights record file does not exist")
        record = json.loads(path.read_text(encoding="utf-8"))
        required_values = {
            "schema_version": 1,
            "record_type": RIGHTS_RECORD_TYPE,
            "resource_group": group["id"],
            "disposition": "approved_for_formal_release",
            "asset_manifest_sha256": asset_manifest_sha256,
        }
        for key, expected in required_values.items():
            if record.get(key) != expected:
                raise ValueError(f"{key} does not match the audited resource group")
        for key in ("grantor", "grantee", "license_identifier", "approved_by"):
            if not isinstance(record.get(key), str) or not record[key].strip():
                raise ValueError(f"{key} must be a non-empty string")
        if record.get("rights_basis") not in RIGHTS_BASES:
            raise ValueError("rights_basis is missing or unsupported")
        scope = record.get("scope")
        if (
            not isinstance(scope, list)
            or not all(isinstance(item, str) for item in scope)
            or not REQUIRED_RIGHTS_SCOPE.issubset(scope)
        ):
            raise ValueError("scope must include formal_release and redistribution")
        approved_at = record.get("approved_at_utc")
        if not isinstance(approved_at, str):
            raise ValueError("approved_at_utc must be an ISO-8601 timestamp")
        try:
            parsed_at = dt.datetime.fromisoformat(approved_at.replace("Z", "+00:00"))
        except ValueError as error:
            raise ValueError("approved_at_utc must be an ISO-8601 timestamp") from error
        if parsed_at.tzinfo is None:
            raise ValueError("approved_at_utc must include a timezone")
        evidence = record.get("evidence")
        if not isinstance(evidence, list) or not evidence:
            raise ValueError("at least one hash-bound evidence file is required")
        verified_evidence = []
        for index, item in enumerate(evidence):
            if not isinstance(item, dict):
                raise ValueError(f"evidence[{index}] must be an object")
            evidence_path = repository_file(repo, item.get("path"), f"evidence[{index}]")
            if not evidence_path.is_file():
                raise ValueError(f"evidence[{index}] file does not exist")
            expected_sha256 = item.get("sha256")
            if not isinstance(expected_sha256, str) or not re.fullmatch(
                r"[0-9a-f]{64}", expected_sha256
            ):
                raise ValueError(f"evidence[{index}] SHA-256 is invalid")
            actual_sha256 = sha256(evidence_path)
            if actual_sha256 != expected_sha256:
                raise ValueError(f"evidence[{index}] SHA-256 mismatch")
            verified_evidence.append({"path": item["path"], "sha256": actual_sha256})
    except (OSError, ValueError, json.JSONDecodeError) as error:
        return {
            "valid": False,
            "status": "invalid",
            "reason": str(error),
            "path": value,
        }
    return {
        "valid": True,
        "status": "approved_and_verified",
        "reason": "policy approval, scope, asset manifest, and evidence hashes verified",
        "path": value,
        "record_sha256": sha256(path),
        "evidence": verified_evidence,
    }


def grade(
    group: dict[str, Any], result: dict[str, Any], rights_validation: dict[str, Any]
) -> tuple[str, str]:
    rights_ok = rights_validation["valid"]
    safety_ok = not group.get("safety_hold", False)
    source_ok = bool(group.get("source"))
    complete = not result["missing_guids"] and bool(result["assets"])
    if not source_ok or not rights_ok or not safety_ok:
        blockers = []
        if not source_ok:
            blockers.append("source provenance missing")
        if not rights_ok:
            blockers.append(f"approved rights record invalid: {rights_validation['reason']}")
        if not safety_ok:
            blockers.append("safety hold")
        return "D", "; ".join(blockers)
    if group.get("intended_runtime") and complete:
        return "A", "technical, provenance, rights, and safety closure complete"
    if group.get("intended_runtime"):
        return "B", "runtime candidate has missing dependencies or format work"
    return "C", "reference-only material is not part of the runtime"


def audit(repo: Path, policy_path: Path) -> dict[str, Any]:
    policy = json.loads(policy_path.read_text(encoding="utf-8"))
    project = repo / "client-restored"
    index = guid_index(project)
    approved = {
        item
        for item in policy.get("approved_rights_records", [])
        if isinstance(item, str)
    }
    groups = []
    all_groups = policy["formal_resource_groups"] + policy.get("reference_groups", [])
    review_groups = []
    for spec in all_groups:
        result = closure(project, spec["seeds"], index)
        inventory, manifest_sha256 = asset_inventory(project, result["assets"])
        rights_validation = validate_rights_record(
            repo, spec, approved, manifest_sha256
        )
        final_grade, reason = grade(spec, result, rights_validation)
        groups.append({
            "id": spec["id"],
            "grade": final_grade,
            "reason": reason,
            "intended_runtime": spec["intended_runtime"],
            "source": spec["source"],
            "fallback": spec["fallback"],
            "rights_record": spec.get("rights_record"),
            "rights_record_validation": rights_validation,
            "seed_paths": spec["seeds"],
            "asset_count": len(result["assets"]),
            "asset_bytes": sum(item["bytes"] for item in inventory),
            "asset_manifest_sha256": manifest_sha256,
            "missing_guid_count": len(result["missing_guids"]),
            "missing_guids": result["missing_guids"],
            "ignored_back_reference_count": len(result["ignored_back_references"]),
            "ignored_back_references": result["ignored_back_references"],
            "dimensions_present": dimensions(result["assets"]),
        })
        if spec["intended_runtime"]:
            review_groups.append({
                "resource_group": spec["id"],
                "disposition": "pending_external_review",
                "source": spec["source"],
                "fallback": spec["fallback"],
                "seed_paths": spec["seeds"],
                "asset_count": len(inventory),
                "asset_bytes": sum(item["bytes"] for item in inventory),
                "asset_manifest_sha256": manifest_sha256,
                "technical_closure": {
                    "missing_guid_count": len(result["missing_guids"]),
                    "missing_guids": result["missing_guids"],
                    "ignored_back_reference_count": len(
                        result["ignored_back_references"]
                    ),
                    "dimensions_present": dimensions(result["assets"]),
                },
                "assets": inventory,
            })
    counts = Counter(item["grade"] for item in groups)
    formal = [item for item in groups if item["intended_runtime"]]
    allowed = bool(formal) and all(item["grade"] == "A" for item in formal)
    approval_inputs = {
        policy_path.relative_to(repo).as_posix(): sha256(policy_path),
    }
    for item in groups:
        validation = item["rights_record_validation"]
        if validation.get("record_sha256") and validation.get("path"):
            approval_inputs[validation["path"]] = validation["record_sha256"]
        for evidence in validation.get("evidence", []):
            approval_inputs[evidence["path"]] = evidence["sha256"]
    return {
        "schemaVersion": 2,
        "generatedAtUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "policyPath": policy_path.relative_to(repo).as_posix(),
        "policySha256": sha256(policy_path),
        "formalBuildAllowed": allowed,
        "gradeCounts": {grade: counts.get(grade, 0) for grade in "ABCD"},
        "formalGroupCount": len(formal),
        "formalAGradeCount": sum(item["grade"] == "A" for item in formal),
        "approvalInputs": [
            {"path": path, "sha256": digest}
            for path, digest in sorted(approval_inputs.items())
        ],
        "groups": groups,
        "_rightsReviewRequest": {
            "schema_version": 1,
            "record_type": "formal_resource_rights_review_request",
            "disposition": "pending_external_review",
            "notice": "This is a review request, not a licence or approval record.",
            "required_approval_record_type": RIGHTS_RECORD_TYPE,
            "required_scope": sorted(REQUIRED_RIGHTS_SCOPE),
            "summary": {
                "resource_groups": len(review_groups),
                "asset_records": sum(item["asset_count"] for item in review_groups),
                "asset_bytes": sum(item["asset_bytes"] for item in review_groups),
                "groups_with_missing_guids": sum(
                    bool(item["technical_closure"]["missing_guid_count"])
                    for item in review_groups
                ),
            },
            "groups": review_groups,
        },
    }


def markdown(report: dict[str, Any]) -> str:
    lines = [
        "# Resource Closure Audit",
        "",
        f"Generated: `{report['generatedAtUtc']}`",
        "",
        f"Formal build allowed: **{str(report['formalBuildAllowed']).lower()}**",
        "",
        "A grade is fail-closed: technical closure alone is insufficient. An approved rights "
        "record and a clear safety state are mandatory. Recovery/diagnostic builds may inspect "
        "D-grade evidence, but the formal build gate rejects it.",
        "",
        "| Group | Grade | Runtime | Assets | Missing GUIDs | Ignored backlinks | Reason |",
        "|---|:---:|:---:|---:|---:|---:|---|",
    ]
    for item in report["groups"]:
        reason = item["reason"].replace("|", "\\|")
        lines.append(f"| `{item['id']}` | {item['grade']} | "
                     f"{'yes' if item['intended_runtime'] else 'no'} | "
                     f"{item['asset_count']} | {item['missing_guid_count']} | "
                     f"{item['ignored_back_reference_count']} | {reason} |")
    lines += ["", "## Formal source and fallback ledger", ""]
    for item in report["groups"]:
        if not item["intended_runtime"]:
            continue
        lines += [
            f"### {item['id']} ({item['grade']})", "",
            f"- Source: {item['source']}",
            f"- Fallback: {item['fallback']}",
            f"- Rights record: `{item['rights_record'] or 'missing'}`",
            f"- Rights validation: `{item['rights_record_validation']['status']}` — "
            f"{item['rights_record_validation']['reason']}",
            f"- Asset manifest SHA-256: `{item['asset_manifest_sha256']}`", "",
        ]
    return "\n".join(lines).rstrip() + "\n"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--policy", type=Path)
    parser.add_argument("--check-formal", action="store_true")
    args = parser.parse_args()
    repo = args.repo.resolve()
    policy = (args.policy or repo / "recovery/resource-closure-policy.json").resolve()
    report = audit(repo, policy)
    rights_request = report.pop("_rightsReviewRequest")
    output_json = repo / "recovery/resource-closure-audit.json"
    output_md = repo / "recovery/RESOURCE_CLOSURE_AUDIT.md"
    rights_request_json = repo / "recovery/rights-review-request.json"
    output_json.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    output_md.write_text(markdown(report), encoding="utf-8")
    rights_request_json.write_text(
        json.dumps(rights_request, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"groups={len(report['groups'])} grades={report['gradeCounts']} "
          f"formalBuildAllowed={report['formalBuildAllowed']}")
    return 0 if (report["formalBuildAllowed"] or not args.check_formal) else 2


if __name__ == "__main__":
    raise SystemExit(main())
