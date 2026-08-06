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
    def test_lighting_scene_owner_reference_is_not_a_runtime_dependency(self):
        with tempfile.TemporaryDirectory() as temporary:
            project = Path(temporary) / "client-restored"
            playable = project / "Assets/PlayableMaps/Test.unity"
            lighting = project / "Assets/Recovered/Test/LightingData.asset"
            source_scene = project / "Assets/Recovered/Test/Source.unity"
            playable.parent.mkdir(parents=True)
            lighting.parent.mkdir(parents=True)
            lighting_guid = "1" * 32
            scene_guid = "2" * 32
            missing_script_guid = "3" * 32
            playable.write_text(
                f"m_LightingDataAsset: {{fileID: 112000000, guid: {lighting_guid}, type: 2}}\n",
                encoding="utf-8",
            )
            lighting.write_text(
                f"m_Scene: {{fileID: 102900000, guid: {scene_guid}, type: 3}}\n",
                encoding="utf-8",
            )
            source_scene.write_text(
                f"m_Script: {{fileID: 11500000, guid: {missing_script_guid}, type: 3}}\n",
                encoding="utf-8",
            )
            lighting.with_suffix(".asset.meta").write_text(
                f"fileFormatVersion: 2\nguid: {lighting_guid}\n",
                encoding="utf-8",
            )
            source_scene.with_suffix(".unity.meta").write_text(
                f"fileFormatVersion: 2\nguid: {scene_guid}\n",
                encoding="utf-8",
            )
            result = subject.closure(
                project,
                ["Assets/PlayableMaps"],
                subject.guid_index(project),
            )
            self.assertEqual([], result["missing_guids"])
            self.assertNotIn(
                "Assets/Recovered/Test/Source.unity",
                result["assets"],
            )
            self.assertEqual(1, len(result["ignored_back_references"]))
            self.assertEqual(scene_guid, result["ignored_back_references"][0]["guid"])

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
