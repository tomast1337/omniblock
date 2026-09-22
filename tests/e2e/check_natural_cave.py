#!/usr/bin/env python3
"""Check the exact/local-LOD visual pair, not cave pixel fidelity."""
import json
from pathlib import Path
import sys


def check(artifacts):
    exact = json.loads((artifacts / "terrain-lod-natural-cave-exact.json").read_text())
    lod = json.loads((artifacts / "terrain-lod-natural-cave-lod.json").read_text())
    before = exact["Quality"]
    after = lod["Quality"]
    if before["Camera"] != after["Camera"]:
        raise ValueError("Exact and LOD captures used different cameras")
    if before["ExactRadiusChunks"] != 8 or after["ExactRadiusChunks"] != 4:
        raise ValueError("The eight-to-four exact-distance handoff was not presented")
    if before["HorizonRadiusChunks"] != 16 or after["HorizonRadiusChunks"] != 16:
        raise ValueError("Expected the bounded sixteen-chunk horizon")
    if after["CachedForestRevision"] != after["PresentationRevision"]:
        raise ValueError("The LOD selection did not use the latest published revision")
    # (24,76,33) belongs to chunk (1,2). Following an exact-distance shrink,
    # resident local column LOD retains ownership; server spatial L2 is not a
    # valid requirement for this same-session before/after comparison.
    rows = [row for row in after["Local"] if row["ChunkX"] == 1
            and row["ChunkZ"] == 2 and row["Layer"] == "solid"]
    if len(rows) != 1:
        raise ValueError("Natural-cave chunk (1,2) had no unique local solid selection")
    column = rows[0]
    if column["SpatialOwned"] or not column["LayerBodyDrawn"]:
        raise ValueError("Natural-cave chunk was not drawn from local column LOD")
    if not column["HasSelectedLayerGeometry"]:
        raise ValueError("Natural-cave chunk selected an empty solid layer")
    if column["SelectedLevel"] != 0 or column["HorizontalSampleBlocks"] != 1:
        raise ValueError("Natural-cave chunk lost its block-scale near LOD")
    if column["SelectedLevel"] not in column["UploadedLevels"]:
        raise ValueError("Natural-cave chunk selected a level that was not uploaded")
    if lod["Coverage"]["HoleCount"] or lod["Coverage"]["OverlapCount"]:
        raise ValueError("Local ownership has holes or overlaps after the handoff")


if __name__ == "__main__":
    try:
        check(Path(sys.argv[1]))
    except (IndexError, KeyError, OSError, TypeError, ValueError) as error:
        sys.exit(f"Natural-cave handoff: {error}")
    print("Natural-cave comparison captured: exact to local column LOD at the same camera")
