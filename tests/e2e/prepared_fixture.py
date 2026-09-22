#!/usr/bin/env python3
"""Owned, path-bound E2E baselines. The shell runner holds the fixture lock across game runs."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import tempfile


OWNER = "omniblock-e2e-prepared-fixture-v1"
SCENARIO = "terrain-lod-generated-patch"


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def inventory(root):
    """Exact content inventory, including empty directories; never follow links."""
    if root.is_symlink() or not root.is_dir():
        raise ValueError(f"Expected a real directory: {root}")
    result = {}
    for directory, directories, files in os.walk(root, followlinks=False):
        for name in sorted(directories + files):
            path = Path(directory) / name
            if path.is_symlink():
                raise ValueError(f"Symlinks are not allowed in fixture data: {path}")
            relative = path.relative_to(root).as_posix()
            if path.is_dir():
                result[relative] = {"directory": True}
            elif path.is_file():
                result[relative] = {"bytes": path.stat().st_size, "sha256": digest(path)}
            else:
                raise ValueError(f"Unsupported fixture file: {path}")
    return result


def initialize(root):
    if root.is_symlink():
        raise ValueError("Fixture root must not be a symlink")
    if root.exists():
        owned(root)
        return
    root.mkdir(parents=True)
    write_json(root / "owner.json", {"owner": OWNER, "root": str(root.resolve())})


def owned(root):
    if root.is_symlink() or not root.is_dir():
        raise ValueError("Fixture root must be an owned, real directory")
    marker = root / "owner.json"
    if marker.is_symlink() or not marker.is_file():
        raise ValueError("Refusing an existing directory without the E2E ownership marker")
    if json.loads(marker.read_text()) != {"owner": OWNER, "root": str(root.resolve())}:
        raise ValueError("Fixture ownership/path mismatch; moved fixtures cannot reuse path-bound LOD caches")
    for name in ("work", "baseline", "previous-work", ".lock", "preparation-contract.json"):
        if (root / name).is_symlink():
            raise ValueError(f"Fixture {name} must not be a symlink")
    for name in ("work", "baseline", "previous-work"):
        if (root / name).exists() and not (root / name).is_dir():
            raise ValueError(f"Fixture {name} must be a directory")


def contract(repo, build):
    # Conservative invalidation: ordinary code/catalog changes require a new fixture, even if
    # its particular seed would happen to produce equivalent terrain. Measurement-script edits
    # deliberately do not invalidate expensive preparation.
    inputs = {}
    for name in ("OmniBlock.dll", "OmniBlock.Client.dll", "OmniBlock.Luau.dll"):
        inputs[name] = digest(build / name)
    inputs["assets"] = inventory(build / "assets")
    for name in (f"{SCENARIO}-prepare.luau", "fixtures/e2e-smoke/level.dat.base64",
                 "fixtures/e2e-flat/level.dat.base64"):
        inputs[name] = digest(repo / "tests/e2e" / name)
    return {"scenario": SCENARIO, "inputs": inputs}


def validate_preparation(artifacts):
    result = json.loads((artifacts / "result.json").read_text())
    if result.get("status") != "passed" or result.get("exitCode") != 0:
        raise ValueError("Only a successful preparation may publish a baseline")
    report = json.loads((artifacts / "terrain-lod-generated-patch-prepared.json").read_text())
    server = report.get("Server") or {}
    identity = report.get("ServerIdentity") or {}
    for name in ("WorldFingerprint", "ContentFingerprint", "GeneratorFingerprint",
                 "MaterialRulesFingerprint", "CompatibilityFingerprint", "RecordFingerprint"):
        value = identity.get(name)
        if not isinstance(value, str) or len(value) != 64 or any(c not in "0123456789abcdef" for c in value):
            raise ValueError(f"Missing/invalid server cache identity: {name}")
    if identity.get("Dimension") != 0 or any(
            not isinstance(identity.get(name), int) or identity[name] <= 0
            for name in ("ReductionSchemaVersion", "MaximumSpatialLevel", "QualityPolicyVersion")):
        raise ValueError("Invalid dimension or LOD schema/policy identity")
    if any(server.get(name) != 0 for name in (
            "PreparationPendingWork", "PreparationFailureEvents", "OfflineSnapshotsDropped")):
        raise ValueError("Preparation has unfinished or failed LOD work")
    if server.get("OfflineSnapshotsSubmitted", 0) <= 0:
        raise ValueError("Preparation did not admit real generated terrain")
    if server.get("SpatialHierarchy", {}).get("PersistenceDeferrals") != 0:
        raise ValueError("Preparation deferred spatial persistence")
    return {"identity": identity, "result": result, "server": server}


def seal(root, expected, artifacts):
    owned(root)
    baseline = root / "baseline"
    if baseline.exists():
        raise ValueError("Baseline already exists; never overwrite it with a measured world")
    if json.loads((root / "preparation-contract.json").read_text()) != expected:
        raise ValueError("Build inputs changed during preparation; refusing to publish")
    evidence = validate_preparation(artifacts)
    source = root / "work/data"
    files = inventory(source)
    if "OmniBlock/saves/e2e-smoke/level.dat" not in files:
        raise ValueError("Prepared save is missing")
    staging = Path(tempfile.mkdtemp(prefix=".seal-", dir=root))
    try:
        shutil.copytree(source, staging / "data")
        if inventory(staging / "data") != files:
            raise ValueError("Preparation data changed during baseline capture")
        manifest = {"schema": 1, "workPath": str((root / "work/data").resolve()),
                    "contract": expected, "files": files, "preparation": evidence}
        write_json(staging / "manifest.json", manifest)
        staging.rename(baseline)  # Publish the data and its manifest together.
    finally:
        if staging.exists():
            shutil.rmtree(staging)


def restore(root, expected):
    owned(root)
    baseline = root / "baseline"
    if not baseline.exists():
        if (root / "work").exists():
            raise ValueError("Incomplete preparation remains; inspect it and select a new fixture directory")
        # Freeze inputs before launching the preparing process, then check again at seal time.
        write_json(root / "preparation-contract.json", expected)
        return "prepare"
    path = baseline / "manifest.json"
    if path.is_symlink():
        raise ValueError("Manifest must not be a symlink")
    manifest = json.loads(path.read_text())
    if manifest.get("schema") != 1 or manifest.get("workPath") != str((root / "work/data").resolve()):
        raise ValueError("Fixture schema/path mismatch; use a new directory")
    if manifest.get("contract") != expected:
        raise ValueError("Fixture build/assets/seed/preparation contract changed; use a new directory")
    if inventory(baseline / "data") != manifest.get("files"):
        raise ValueError("Fixture data was changed or corrupted; refusing reuse")
    previous = root / "previous-work"
    if previous.exists():
        raise ValueError("Interrupted restore remains in previous-work; inspect before proceeding")
    staging = Path(tempfile.mkdtemp(prefix=".restore-", dir=root))
    work = root / "work"
    try:
        shutil.copytree(baseline / "data", staging / "data")
        if inventory(staging / "data") != manifest["files"]:
            raise ValueError("Baseline changed during restore")
        if work.exists():
            work.rename(previous)
        try:
            staging.rename(work)
        except OSError:
            if previous.exists():
                previous.rename(work)
            raise
        # Only the previous disposable workspace is removed. The sealed baseline never changes.
        if previous.exists():
            shutil.rmtree(previous)
    finally:
        if staging.exists():
            shutil.rmtree(staging)
    return "reuse"


def verify(root, expected, artifacts):
    """Prove the measured process actually reopened the prepared world's cache identity."""
    owned(root)
    manifest = json.loads((root / "baseline/manifest.json").read_text())
    if manifest["contract"] != expected:
        raise ValueError("Build inputs changed during measurement")
    result = json.loads((artifacts / "result.json").read_text())
    if result.get("status") != "passed" or result.get("exitCode") != 0:
        raise ValueError("Measured run did not pass")
    report = json.loads((artifacts / "terrain-lod-generated-patch-return-ready.json").read_text())
    if report.get("ServerIdentity") != manifest["preparation"]["identity"]:
        raise ValueError("Measured server identity differs from the prepared baseline")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("init", "restore", "seal", "verify"))
    parser.add_argument("root", type=Path)
    parser.add_argument("--repo", type=Path)
    parser.add_argument("--build", type=Path)
    parser.add_argument("--artifacts", type=Path)
    args = parser.parse_args()
    root = args.root.absolute()
    try:
        if args.action == "init":
            initialize(root)
        else:
            if args.repo is None or args.build is None:
                raise ValueError("--repo and --build are required for restore/seal/verify")
            if args.action in ("seal", "verify") and args.artifacts is None:
                raise ValueError("--artifacts is required for seal/verify")
            expected = contract(args.repo, args.build)
            if args.action == "restore":
                print(restore(root, expected))
            elif args.action == "seal":
                seal(root, expected, args.artifacts)
            else:
                verify(root, expected, args.artifacts)
    except (ValueError, OSError, KeyError, TypeError, AttributeError) as error:
        parser.exit(2, f"Prepared fixture: {error}\n")


if __name__ == "__main__":
    main()
