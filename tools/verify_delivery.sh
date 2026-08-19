#!/bin/bash

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mode="quick"

usage() {
  echo "Usage: tools/verify_delivery.sh [--quick|--full]"
  echo ""
  echo "  --quick  Validate pinned tools, install the locked Node graph, and run"
  echo "           TypeScript/Vitest plus all Python safety tests."
  echo "  --full   Also run Unity EditMode, PlayMode, final regression gates, and"
  echo "           a diagnostic WebGL build with a SHA-256 file-tree manifest."
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --quick) mode="quick" ;;
    --full) mode="full" ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage >&2; exit 2 ;;
  esac
  shift
done

expected_node="$(tr -d '[:space:]' < "$repository_root/.node-version")"
expected_python="$(tr -d '[:space:]' < "$repository_root/.python-version")"
expected_pnpm="11.9.0"
expected_unity="2022.3.62f3c1"
expected_dotnet="10.0.301"
python_bin="${PYTHON_BIN:-python3}"
unity_editor="${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/$expected_unity/Unity.app/Contents/MacOS/Unity}"
output_dir="${DELIVERY_OUTPUT_DIR:-$repository_root/artifacts/delivery-verification}"

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Required command not found: $1" >&2
    exit 1
  fi
}

require_exact_version() {
  local label="$1"
  local expected="$2"
  local actual="$3"
  if [[ "$actual" != "$expected" ]]; then
    echo "$label version mismatch: expected $expected, got $actual" >&2
    exit 1
  fi
  echo "$label $actual"
}

require_command node
require_command pnpm
require_command "$python_bin"
require_command dotnet

require_exact_version "Node.js" "$expected_node" "$(node --version | sed 's/^v//')"
require_exact_version "pnpm" "$expected_pnpm" "$(pnpm --version)"
require_exact_version "Python" "$expected_python" "$($python_bin --version 2>&1 | awk '{print $2}')"
require_exact_version ".NET SDK" "$expected_dotnet" "$(dotnet --version)"

project_unity="$(awk '/m_EditorVersion:/ {print $2; exit}' "$repository_root/client-restored/ProjectSettings/ProjectVersion.txt")"
require_exact_version "Unity project" "$expected_unity" "$project_unity"

mkdir -p "$output_dir"
cd "$repository_root"

pnpm install --frozen-lockfile
pnpm build 2>&1 | tee "$output_dir/server-build.log"
pnpm test 2>&1 | tee "$output_dir/server-test.log"
"$python_bin" -m unittest discover -s tools -p 'test_*.py' 2>&1 | tee "$output_dir/python-tools-test.log"

unreal_source_key="MapPreview-Android_Multi__ee15b61ab0c0"
unreal_analysis_root="$repository_root/recovery/special-formats/unreal-analysis/$unreal_source_key"
unreal_extracted_root="$repository_root/recovery/special-formats/unreal-extracted/$unreal_source_key"
unreal_json_root="$repository_root/recovery/special-formats/unreal-object-json/$unreal_source_key"
dotnet build tools/UnrealAssetAudit/UnrealAssetAudit.csproj --configuration Release \
  2>&1 | tee "$output_dir/unreal-asset-audit-build.log"
dotnet run --project tools/UnrealAssetAudit/UnrealAssetAudit.csproj \
  --configuration Release --no-build -- \
  --closure "$unreal_analysis_root/package-closure-v2.json" \
  --extracted-root "$unreal_extracted_root" \
  --json-output-root "$unreal_json_root" \
  --output "$unreal_analysis_root/uassetapi-audit.json" \
  2>&1 | tee "$output_dir/unreal-asset-audit.log"
"$python_bin" - "$unreal_analysis_root/uassetapi-audit.json" "$unreal_json_root" <<'PY'
import hashlib
import json
import pathlib
import sys

report_path = pathlib.Path(sys.argv[1])
output_root = pathlib.Path(sys.argv[2])
report = json.loads(report_path.read_text(encoding="utf-8"))
summary = report["summary"]
expected = summary["candidates"]
if expected != 518 or summary["structural_parsed"] != expected or summary["full_parsed"] != expected:
    raise SystemExit(f"Unreal UObject audit is incomplete: {summary}")
for record in report["records"]:
    full = record["full_parse"]
    if full["status"] != "parsed" or full.get("binary_equality_verified") is not True:
        raise SystemExit(f"Unreal UObject binary equality failed: {record['path']}")
    output = full["json_output"]
    target = output_root / output["path"]
    data = target.read_bytes()
    if len(data) != output["bytes"] or hashlib.sha256(data).hexdigest() != output["sha256"]:
        raise SystemExit(f"Unreal UObject JSON provenance failed: {target}")
print(f"Unreal UObject audit passed: {expected}/{expected}, binary equality and JSON hashes verified")
PY

if [[ "$mode" == "quick" ]]; then
  echo "Quick delivery verification passed. Logs: $output_dir"
  exit 0
fi

if [[ ! -x "$unity_editor" ]]; then
  echo "Pinned Unity editor not found or not executable: $unity_editor" >&2
  exit 1
fi

unity_project="$repository_root/client-restored"
run_unity_tests() {
  local platform="$1"
  local result_path="$2"
  local log_path="$3"
  local deadline
  local unity_pid

  # Remove only the ignored, generated result so a stale XML cannot satisfy
  # this run. Unity 2022.3 China may keep the EditMode process alive after it
  # has atomically written a complete result; the loop terminates that exact
  # batch process only after the XML parses successfully.
  rm -f "$result_path"
  "$unity_editor" -batchmode -projectPath "$unity_project" \
    -runTests -testPlatform "$platform" \
    -testResults "$result_path" \
    -logFile "$log_path" &
  unity_pid=$!
  deadline=$((SECONDS + 900))

  while kill -0 "$unity_pid" 2>/dev/null; do
    if [[ -s "$result_path" ]] && "$python_bin" - "$result_path" <<'PY'
import sys
import xml.etree.ElementTree as ET
ET.parse(sys.argv[1])
PY
    then
      kill -TERM "$unity_pid" 2>/dev/null || true
      wait "$unity_pid" 2>/dev/null || true
      return 0
    fi
    if (( SECONDS >= deadline )); then
      kill -TERM "$unity_pid" 2>/dev/null || true
      wait "$unity_pid" 2>/dev/null || true
      echo "Unity $platform tests timed out before writing a result: $result_path" >&2
      return 1
    fi
    sleep 2
  done
  wait "$unity_pid"
}

run_unity_tests editmode "$output_dir/unity-editmode.xml" "$output_dir/unity-editmode.log"
run_unity_tests playmode "$output_dir/unity-playmode.xml" "$output_dir/unity-playmode.log"

"$python_bin" - "$output_dir/unity-editmode.xml" "$output_dir/unity-playmode.xml" <<'PY'
import sys
import xml.etree.ElementTree as ET

for path in sys.argv[1:]:
    root = ET.parse(path).getroot()
    total = int(root.attrib.get("total", root.attrib.get("testcasecount", "0")))
    failed = int(root.attrib.get("failed", root.attrib.get("failures", "0")))
    result = root.attrib.get("result", "")
    if total <= 0 or failed != 0 or result.lower() not in {"passed", "success"}:
        raise SystemExit(
            f"Unity test result did not pass: {path}: total={total}, failed={failed}, result={result}"
        )
    print(f"Unity tests passed: {path}: total={total}")
PY

"$unity_editor" -batchmode -quit -projectPath "$unity_project" \
  -executeMethod GenesisFinalRegressionAudit.Run \
  -logFile "$output_dir/unity-final-regression.log"

"$python_bin" - "$repository_root/recovery/final-automated-regression.json" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    report = json.load(handle)
if not report.get("passed") or len(report.get("completedGates", [])) != 9:
    raise SystemExit(f"Final Unity regression gate failed or was incomplete: {sys.argv[1]}")
print("Unity final regression gates passed: 9/9")
PY

GENESIS_DIAGNOSTIC=1 "$unity_editor" -batchmode -quit \
  -projectPath "$unity_project" \
  -executeMethod GenesisRestoredBuild.BuildRestoredWebGL \
  -logFile "$output_dir/unity-webgl-build.log"

webgl_root="$unity_project/Build/WebGL"
if [[ ! -f "$webgl_root/index.html" || ! -f "$webgl_root/Build/WebGL.wasm" ]]; then
  echo "WebGL entry point or wasm missing after successful Unity invocation: $webgl_root" >&2
  exit 1
fi

manifest="$output_dir/webgl-tree.sha256"
(
  cd "$webgl_root"
  find . -type f -print0 | LC_ALL=C sort -z | xargs -0 shasum -a 256
) > "$manifest"
shasum -a 256 "$manifest" > "$output_dir/webgl-tree-manifest.sha256"

echo "Full delivery verification passed. Logs and manifest: $output_dir"
