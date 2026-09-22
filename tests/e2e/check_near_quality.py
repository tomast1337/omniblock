#!/usr/bin/env python3
"""Narrow empty-layer regression gate; not a whole-scene visual-quality/convergence assertion."""
import json
from pathlib import Path
import sys


def check(artifacts):
    snapshot = json.loads((artifacts / "terrain-lod-near-quality-lod-settled.json").read_text())
    quality = snapshot["Quality"]
    camera = quality["Camera"]
    if (camera["X"], camera["Z"]) != (8, -8) or quality["ExactRadiusChunks"] != 4:
        raise ValueError("Expected the fixed near-quality camera and four-chunk exact radius")
    rows = {(r["ChunkX"], r["ChunkZ"], r["Layer"]): r for r in quality["Local"]}
    for x in (0, 1):
        for layer in ("solid", "translucent"):
            row = rows[(x, 4, layer)]
            if row["SpatialOwned"] or 0 not in row["UploadedLevels"]:
                raise ValueError(f"Column {x},4 {layer}: fixture no longer exercises compiled fine local LOD")
            if row["SelectedLevel"] != 0:
                raise ValueError(f"Column {x},4 {layer}: selected L{row['SelectedLevel']} instead of L0")
    empty = rows[(0, 4, "translucent")]
    water = rows[(1, 4, "translucent")]
    if empty["HasSelectedLayerGeometry"] or 0 in empty["LevelsWithLayerGeometry"]:
        raise ValueError("Arch column must exercise an intentionally empty fine translucent layer")
    if empty["LayerBodyDrawn"]:
        raise ValueError("Empty fine translucent layer must not submit body geometry")
    if not empty["LevelsWithLayerGeometry"]:
        raise ValueError("Arch column must retain coarse translucent geometry to test fallback rejection")
    if not water["HasSelectedLayerGeometry"] or 0 not in water["LevelsWithLayerGeometry"]:
        raise ValueError("Adjacent basin must keep its real fine water geometry")
    if not water["LayerBodyDrawn"]:
        raise ValueError("Adjacent basin's fine water must actually be submitted")


if __name__ == "__main__":
    try:
        check(Path(sys.argv[1]))
    except (IndexError, KeyError, OSError, ValueError) as error:
        sys.exit(f"Near-quality layer regression: {error}")
    print("Near-quality layer regression passed (empty L0 retained; adjacent water remains L0)")
