"""No game/native dependencies: exercise the disposable-workspace ownership contract."""
import copy
import fcntl
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import prepared_fixture as fixture


class PreparedFixtureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.parent = Path(self.temp.name)
        self.root = self.parent / "fixture"
        fixture.initialize(self.root)
        self.expected = {"scenario": fixture.SCENARIO, "inputs": {"build": "original"}}
        self.assertEqual("prepare", fixture.restore(self.root, self.expected))
        self.data = self.root / "work/data"
        self.level = self.data / "OmniBlock/saves/e2e-smoke/level.dat"
        self.level.parent.mkdir(parents=True)
        self.level.write_bytes(b"original world")
        (self.data / "empty").mkdir()
        self.artifacts = self.parent / "artifacts"
        self.artifacts.mkdir()
        fixture.write_json(self.artifacts / "result.json", {"status": "passed", "exitCode": 0})
        self.report = {
            "ServerIdentity": {
                **{key: "a" * 64 for key in (
                    "WorldFingerprint", "ContentFingerprint", "GeneratorFingerprint",
                    "MaterialRulesFingerprint", "CompatibilityFingerprint", "RecordFingerprint")},
                "Dimension": 0, "ReductionSchemaVersion": 1,
                "MaximumSpatialLevel": 10, "QualityPolicyVersion": 1,
            },
            "Server": {
                "PreparationPendingWork": 0, "PreparationFailureEvents": 0,
                "OfflineSnapshotsDropped": 0, "OfflineSnapshotsSubmitted": 1800,
                "SpatialHierarchy": {"PersistenceDeferrals": 0},
            },
        }
        self.write_report()

    def write_report(self):
        fixture.write_json(self.artifacts / "terrain-lod-generated-patch-prepared.json", self.report)

    def seal(self):
        fixture.seal(self.root, self.expected, self.artifacts)

    def test_restore_removes_measurement_changes_without_mutating_baseline(self):
        self.seal()
        baseline = fixture.inventory(self.root / "baseline")
        self.level.write_bytes(b"edited world")
        (self.data / "new-file").write_text("measurement")
        (self.data / "empty").rmdir()
        self.assertEqual("reuse", fixture.restore(self.root, self.expected))
        self.assertEqual(b"original world", self.level.read_bytes())
        self.assertFalse((self.data / "new-file").exists())
        self.assertTrue((self.data / "empty").is_dir())
        self.assertEqual(baseline, fixture.inventory(self.root / "baseline"))
        manifest = json.loads((self.root / "baseline/manifest.json").read_text())
        self.assertEqual(str(self.data.resolve()), manifest["workPath"])
        self.assertEqual(self.report["ServerIdentity"], manifest["preparation"]["identity"])
        self.assertFalse((self.root / "previous-work").exists())

    def test_sealed_baseline_cannot_be_replaced(self):
        self.seal()
        with self.assertRaisesRegex(ValueError, "already exists"):
            self.seal()

    def test_measured_server_must_reopen_prepared_identity(self):
        self.seal()
        report_path = self.artifacts / "terrain-lod-generated-patch-return-ready.json"
        fixture.write_json(report_path, self.report)
        fixture.verify(self.root, self.expected, self.artifacts)
        for key in self.report["ServerIdentity"]:
            with self.subTest(key=key):
                report = copy.deepcopy(self.report)
                del report["ServerIdentity"][key]
                fixture.write_json(report_path, report)
                with self.assertRaisesRegex(ValueError, "identity differs"):
                    fixture.verify(self.root, self.expected, self.artifacts)

    def test_changed_inputs_rejected_before_workspace_is_modified(self):
        self.seal()
        before = fixture.inventory(self.root)
        with self.assertRaisesRegex(ValueError, "contract changed"):
            fixture.restore(self.root, {**self.expected, "inputs": {"build": "different"}})
        self.assertEqual(before, fixture.inventory(self.root))

    def test_changed_inputs_during_preparation_cannot_be_sealed(self):
        with self.assertRaisesRegex(ValueError, "during preparation"):
            fixture.seal(self.root, {**self.expected, "inputs": {}}, self.artifacts)
        self.assertFalse((self.root / "baseline").exists())

    def test_failed_preparation_not_published(self):
        fixture.write_json(self.artifacts / "result.json", {"status": "failed", "exitCode": 1})
        with self.assertRaisesRegex(ValueError, "successful preparation"):
            self.seal()
        self.assertFalse((self.root / "baseline").exists())

    def test_incomplete_identity_not_published(self):
        del self.report["ServerIdentity"]["GeneratorFingerprint"]
        self.write_report()
        with self.assertRaisesRegex(ValueError, "GeneratorFingerprint"):
            self.seal()

    def test_unfinished_or_failed_work_not_published(self):
        original = copy.deepcopy(self.report)
        for key in ("PreparationPendingWork", "PreparationFailureEvents", "OfflineSnapshotsDropped"):
            with self.subTest(key=key):
                self.report = copy.deepcopy(original)
                self.report["Server"][key] = 1
                self.write_report()
                with self.assertRaisesRegex(ValueError, "unfinished or failed"):
                    self.seal()
        self.report = copy.deepcopy(original)
        self.report["Server"]["SpatialHierarchy"]["PersistenceDeferrals"] = 1
        self.write_report()
        with self.assertRaisesRegex(ValueError, "deferred"):
            self.seal()
        self.assertFalse((self.root / "baseline").exists())

    def test_corrupted_missing_or_extra_baseline_file_rejected(self):
        self.seal()
        baseline_file = self.root / "baseline/data/OmniBlock/saves/e2e-smoke/level.dat"
        baseline_file.write_bytes(b"bad bytes")
        with self.assertRaisesRegex(ValueError, "corrupted"):
            fixture.restore(self.root, self.expected)
        baseline_file.unlink()
        with self.assertRaisesRegex(ValueError, "corrupted"):
            fixture.restore(self.root, self.expected)
        baseline_file.write_bytes(b"original world")
        (self.root / "baseline/data/extra").write_text("extra")
        with self.assertRaisesRegex(ValueError, "corrupted"):
            fixture.restore(self.root, self.expected)
        self.assertEqual(b"original world", self.level.read_bytes())

    def test_incomplete_preparation_rejected(self):
        with self.assertRaisesRegex(ValueError, "Incomplete preparation"):
            fixture.restore(self.root, self.expected)

    def test_interrupted_restore_rejected(self):
        self.seal()
        (self.root / "previous-work").mkdir()
        with self.assertRaisesRegex(ValueError, "Interrupted restore"):
            fixture.restore(self.root, self.expected)

    def test_unowned_directory_is_not_modified(self):
        unowned = self.parent / "real-save"
        unowned.mkdir()
        (unowned / "data").write_text("important")
        with self.assertRaisesRegex(ValueError, "ownership"):
            fixture.initialize(unowned)
        self.assertEqual(["data"], [p.name for p in unowned.iterdir()])

    def test_runner_rejects_concurrent_use_before_building_or_restoring(self):
        with (self.root / ".lock").open("w") as lock:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            runner = Path(__file__).with_name("run-local.sh")
            result = subprocess.run(
                ["bash", str(runner), fixture.SCENARIO],
                env={**os.environ, "E2E_PREPARED_FIXTURE": str(self.root)},
                capture_output=True, text=True, timeout=10)
        self.assertEqual(2, result.returncode)
        self.assertIn("already in use", result.stderr)
        self.assertNotIn("Running OmniBlock", result.stdout)
        self.assertEqual(b"original world", self.level.read_bytes())

    def test_moved_fixture_rejected(self):
        moved = self.parent / "moved"
        self.root.rename(moved)
        with self.assertRaisesRegex(ValueError, "path mismatch"):
            fixture.restore(moved, self.expected)

    def test_symlink_data_rejected(self):
        outside = self.parent / "outside"
        outside.write_text("untouched")
        (self.data / "link").symlink_to(outside)
        with self.assertRaisesRegex(ValueError, "Symlinks"):
            self.seal()
        self.assertEqual("untouched", outside.read_text())

    def test_symlink_baseline_and_root_rejected(self):
        (self.root / "baseline").symlink_to(self.parent, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "symlink"):
            fixture.restore(self.root, self.expected)
        link = self.parent / "root-link"
        link.symlink_to(self.root, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, "symlink"):
            fixture.initialize(link)

    def test_copy_failure_never_publishes_baseline(self):
        with patch.object(fixture.shutil, "copytree", side_effect=OSError("disk full")):
            with self.assertRaisesRegex(OSError, "disk full"):
                self.seal()
        self.assertFalse((self.root / "baseline").exists())
        self.assertFalse(list(self.root.glob(".seal-*")))
        self.assertTrue(self.level.exists())

    def test_failed_restore_install_rolls_back_work(self):
        self.seal()
        self.level.write_bytes(b"diagnostic work")
        rename = Path.rename

        def fail_install(path, target):
            if path.name.startswith(".restore-"):
                raise OSError("install failed")
            return rename(path, target)

        with patch.object(Path, "rename", fail_install):
            with self.assertRaisesRegex(OSError, "install failed"):
                fixture.restore(self.root, self.expected)
        self.assertEqual(b"diagnostic work", self.level.read_bytes())
        self.assertFalse(list(self.root.glob(".restore-*")))
        self.assertFalse((self.root / "previous-work").exists())

    def test_contract_tracks_build_assets_seed_and_preparer_not_measurement(self):
        repo, build = self.parent / "repo", self.parent / "build"
        paths = [build / name for name in ("OmniBlock.dll", "OmniBlock.Client.dll", "OmniBlock.Luau.dll")]
        paths += [build / "assets/blocks.json"]
        paths += [repo / "tests/e2e" / name for name in (
            f"{fixture.SCENARIO}-prepare.luau", "fixtures/e2e-smoke/level.dat.base64",
            "fixtures/e2e-flat/level.dat.base64")]
        for path in paths:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("original")
        expected = fixture.contract(repo, build)
        for path in paths:
            with self.subTest(path=path):
                path.write_text("changed")
                self.assertNotEqual(expected, fixture.contract(repo, build))
                path.write_text("original")
        (repo / "tests/e2e" / f"{fixture.SCENARIO}.luau").write_text("new assertions")
        self.assertEqual(expected, fixture.contract(repo, build))


if __name__ == "__main__":
    unittest.main()
