import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

MODULE_PATH = Path(__file__).with_name("audit_resource_closures.py")
SPEC = importlib.util.spec_from_file_location("audit_resource_closures", MODULE_PATH)
subject = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(subject)


class ResourceClosureAuditTests(unittest.TestCase):
    def test_rights_missing_forces_d_even_when_dependency_closure_is_complete(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            assets = root / "client-restored/Assets/Resources/Test"
            assets.mkdir(parents=True)
            (assets / "mesh.asset").write_text("%YAML 1.1\n", encoding="utf-8")
            policy = root / "policy.json"
            policy.write_text(json.dumps({
                "approved_rights_records": [],
                "formal_resource_groups": [{
                    "id": "test", "seeds": ["Assets/Resources/Test"],
                    "source": "fixture", "fallback": "primitive", "rights_record": None,
                    "intended_runtime": True,
                }],
                "reference_groups": [],
            }), encoding="utf-8")
            report = subject.audit(root, policy)
            self.assertEqual("D", report["groups"][0]["grade"])
            self.assertFalse(report["formalBuildAllowed"])

    def test_approved_complete_runtime_group_is_a(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            assets = root / "client-restored/Assets/Resources/Test"
            assets.mkdir(parents=True)
            (assets / "clip.wav").write_bytes(b"RIFFfixture")
            policy = root / "policy.json"
            policy.write_text(json.dumps({
                "approved_rights_records": ["rights/test.json"],
                "formal_resource_groups": [{
                    "id": "test", "seeds": ["Assets/Resources/Test"],
                    "source": "fixture", "fallback": "silence",
                    "rights_record": "rights/test.json", "intended_runtime": True,
                }],
                "reference_groups": [],
            }), encoding="utf-8")
            report = subject.audit(root, policy)
            self.assertEqual("A", report["groups"][0]["grade"])
            self.assertTrue(report["formalBuildAllowed"])


if __name__ == "__main__":
    unittest.main()
