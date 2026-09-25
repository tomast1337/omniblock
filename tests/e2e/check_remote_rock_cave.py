#!/usr/bin/env python3
"""Check the persisted rock-cave LOD/exact ownership pair.

The Luau scenario checks actual source and mesh-policy air beneath a stone roof;
this checker verifies the same-camera presentation metadata and captures. It
does not claim that a particular screenshot pixel has been identified.
"""

import json
from pathlib import Path
import struct
import sys


def dimensions(path):
    png = path.read_bytes()
    if len(png) < 24 or png[:8] != b"\x89PNG\r\n\x1a\n" or png[12:16] != b"IHDR":
        raise ValueError(f"Invalid PNG screenshot: {path}")
    return struct.unpack_from(">II", png, 16)


def check(artifacts):
    spatial = json.loads((artifacts / "terrain-lod-rock-cave-spatial.json").read_text())
    exact = json.loads((artifacts / "terrain-lod-rock-cave-exact.json").read_text())
    settled = json.loads((artifacts / "terrain-lod-rock-cave-exact-settled.json").read_text())
    first, second, third = (sample["Quality"] for sample in (spatial, exact, settled))
    if (first["Camera"] != second["Camera"] or first["Camera"] != third["Camera"] or
            spatial["View"] != exact["View"] or spatial["View"] != settled["View"] or
            first["VerticalFov"] != second["VerticalFov"] or
            first["VerticalFov"] != third["VerticalFov"]):
        raise ValueError("LOD and exact cave captures moved the camera or changed FOV")
    if tuple(quality["ExactRadiusChunks"] for quality in (first, second, third)) != (4, 14, 14):
        raise ValueError("Expected four-to-fourteen exact-radius handoff")
    if any(quality["HorizonRadiusChunks"] != 16 for quality in (first, second, third)):
        raise ValueError("Horizon changed during rock-cave comparison")
    if first["CachedForestRevision"] != first["PresentationRevision"]:
        raise ValueError("Spatial capture used a stale selection forest")
    cave = [row for row in first["Spatial"] if row["Tile"]["Level"] == 2 and
            row["Tile"]["X"] == 0 and row["Tile"]["Z"] == 0]
    if (len(cave) != 1 or not cave[0]["Authoritative"] or
            not cave[0]["CpuSourceMatchesPublishedMesh"] or
            cave[0]["Mesh"]["HorizontalSampleBlocks"] != 1 or
            cave[0]["SelectedSolidPages"] == 0):
        raise ValueError("Stone-roof cave tile was not authoritative selected 1x1 LOD")
    for label, sample in (("spatial", spatial), ("exact", exact), ("settled", settled)):
        if sample["Coverage"]["HoleCount"] or sample["Coverage"]["OverlapCount"]:
            raise ValueError(f"{label} capture had a near-field ownership defect")
    if exact["Coverage"]["ExactOwnedColumns"] <= spatial["Coverage"]["ExactOwnedColumns"]:
        raise ValueError("Exact radius expansion did not increase exact ownership")
    screenshots = sorted((artifacts / "screenshots").glob("*.png"))
    if len(screenshots) != 3 or len({dimensions(path) for path in screenshots}) != 1:
        raise ValueError("Expected three same-resolution cave comparison screenshots")
    return cave[0]["PublishedHash"], dimensions(screenshots[0])


if __name__ == "__main__":
    try:
        source_hash, image_size = check(Path(sys.argv[1]))
    except (IndexError, KeyError, OSError, TypeError, ValueError) as error:
        sys.exit(f"Remote rock cave comparison: {error}")
    print(f"Remote rock cave source/mesh/exact handoff passed; "
          f"source {source_hash[:12]}, image {image_size[0]}x{image_size[1]}")
