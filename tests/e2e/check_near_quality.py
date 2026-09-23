#!/usr/bin/env python3
"""Narrow empty-layer regression gate; not a whole-scene visual-quality/convergence assertion."""
import json
from pathlib import Path
import sys


def check(artifacts):
    exact = json.loads((artifacts / "terrain-lod-near-quality-exact-reference.json").read_text())
    exact_zoom = json.loads((artifacts / "terrain-lod-near-quality-exact-zoom.json").read_text())
    snapshot = json.loads((artifacts / "terrain-lod-near-quality-lod-settled.json").read_text())
    lod_zoom = json.loads((artifacts / "terrain-lod-near-quality-lod-zoom.json").read_text())
    quality = snapshot["Quality"]
    camera = quality["Camera"]
    if (camera["X"], camera["Z"]) != (8, -8) or quality["ExactRadiusChunks"] != 4:
        raise ValueError("Expected the fixed near-quality camera and four-chunk exact radius")
    views = [view["Quality"] for view in (exact, exact_zoom, snapshot, lod_zoom)]
    if any(view["Camera"] != camera or view["HorizonRadiusChunks"] != 16 for view in views):
        raise ValueError("Exact and LOD close-ups must share one camera and horizon")
    if [view["ExactRadiusChunks"] for view in views] != [8, 8, 4, 4]:
        raise ValueError("Close-ups did not preserve the exact-to-LOD handoff")
    if not (views[1]["VerticalFov"] < views[0]["VerticalFov"] and
            views[3]["VerticalFov"] < views[2]["VerticalFov"]):
        raise ValueError("Close-ups must use a narrower field of view than their references")
    if abs(views[1]["VerticalFov"] - views[3]["VerticalFov"]) > 0.01:
        raise ValueError("Exact and LOD close-ups must have matching fields of view")
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
    zoom_rows = {(r["ChunkX"], r["ChunkZ"], r["Layer"]): r for r in views[3]["Local"]}
    for key in ((0, 4, "solid"), (1, 4, "translucent")):
        row = zoom_rows.get(key)
        if row is None or row["SpatialOwned"] or row["SelectedLevel"] != 0 or not row["LayerBodyDrawn"]:
            raise ValueError(f"Close-up lost the drawn local L0 landmark layer {key}")


if __name__ == "__main__":
    try:
        check(Path(sys.argv[1]))
    except (IndexError, KeyError, OSError, ValueError) as error:
        sys.exit(f"Near-quality layer regression: {error}")
    print("Near-quality close-up passed (matching camera/FOV; drawn local L0 landmarks remain)")
