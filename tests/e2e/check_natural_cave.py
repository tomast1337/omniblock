#!/usr/bin/env python3
"""Check the exact/local-LOD visual pair, not cave pixel fidelity."""
import json
from pathlib import Path
import sys


def check(artifacts):
    exact = json.loads((artifacts / "terrain-lod-natural-cave-exact.json").read_text())
    fine_before = json.loads((artifacts / "terrain-lod-natural-cave-fine-before.json").read_text())
    coarse = json.loads((artifacts / "terrain-lod-natural-cave-coarse.json").read_text())
    lod = json.loads((artifacts / "terrain-lod-natural-cave-lod.json").read_text())
    exact_zoom = json.loads((artifacts / "terrain-lod-natural-cave-exact-zoom.json").read_text())
    lod_zoom = json.loads((artifacts / "terrain-lod-natural-cave-lod-zoom.json").read_text())
    before = exact["Quality"]
    initial = fine_before["Quality"]
    middle = coarse["Quality"]
    after = lod["Quality"]
    zoom_before = exact_zoom["Quality"]
    zoom_after = lod_zoom["Quality"]
    if len({(q["Camera"]["X"], q["Camera"]["Y"], q["Camera"]["Z"])
            for q in (before, initial, middle, after, zoom_before, zoom_after)}) != 1:
        raise ValueError("Exact and LOD captures used different cameras")
    camera = before["Camera"]
    if camera["X"] != 24 or camera["Z"] != -70 or not 114 < camera["Y"] < 118:
        raise ValueError("The natural-cave comparison camera moved from its fixed fixture")
    if (before["ExactRadiusChunks"], initial["ExactRadiusChunks"],
            middle["ExactRadiusChunks"], after["ExactRadiusChunks"]) != (8, 4, 4, 4):
        raise ValueError("The eight-to-four exact-distance handoff was not presented")
    if any(snapshot["HorizonRadiusChunks"] != 16 for snapshot in (before, initial, middle, after)):
        raise ValueError("Expected the bounded sixteen-chunk horizon")
    if (zoom_before["ExactRadiusChunks"], zoom_after["ExactRadiusChunks"]) != (8, 4):
        raise ValueError("Zoomed natural-opening pair changed the exact-to-LOD handoff")
    if not (zoom_before["VerticalFov"] < before["VerticalFov"] and
            zoom_after["VerticalFov"] < after["VerticalFov"]):
        raise ValueError("Natural-opening close-ups are not narrower than the reference views")
    if abs(zoom_before["VerticalFov"] - zoom_after["VerticalFov"]) > 0.01:
        raise ValueError("Natural-opening exact and LOD close-ups used different fields of view")
    if (initial["LocalDropoffScale"], middle["LocalDropoffScale"],
            after["LocalDropoffScale"]) != (1, 0.75, 1):
        raise ValueError("Expected the 1x, 0.75x, 1x presentation-quality comparison")
    if after["CachedForestRevision"] != after["PresentationRevision"]:
        raise ValueError("The LOD selection did not use the latest published revision")
    # (24,76,33) belongs to chunk (1,2). Following an exact-distance shrink,
    # resident local column LOD retains ownership; server spatial L2 is not a
    # valid requirement for this same-session before/after comparison.
    def cave_column(quality):
        rows = [row for row in quality["Local"] if row["ChunkX"] == 1
                and row["ChunkZ"] == 2 and row["Layer"] == "solid"]
        if len(rows) != 1:
            raise ValueError("Natural-cave chunk (1,2) had no unique local solid selection")
        column = rows[0]
        if column["SpatialOwned"] or not column["LayerBodyDrawn"]:
            raise ValueError("Natural-cave chunk was not drawn from local column LOD")
        if not column["HasSelectedLayerGeometry"] or column["SelectedLayerVertices"] <= 0:
            raise ValueError("Natural-cave chunk selected an empty solid layer")
        if column["SelectedLevel"] not in column["UploadedLevels"]:
            raise ValueError("Natural-cave chunk selected a level that was not uploaded")
        return column

    first = cave_column(initial)
    old = cave_column(middle)
    new = cave_column(after)
    close = cave_column(zoom_after)
    if close["SelectedLevel"] != 0 or close["TerrainRevision"] != new["TerrainRevision"]:
        raise ValueError("Natural-opening close-up lost the drawn local 1x1 cave source")
    same_revision = len({first["TerrainRevision"], old["TerrainRevision"], new["TerrainRevision"]}) == 1
    same_levels = first["UploadedLevels"] == old["UploadedLevels"] == new["UploadedLevels"]
    if not same_revision or not same_levels:
        raise ValueError("The two quality selections used different terrain or uploaded levels")
    if (first["SelectedLevel"], old["SelectedLevel"], new["SelectedLevel"]) != (0, 1, 0):
        raise ValueError("The cave did not select 1x1, 2x2, 1x1 at the same camera")
    if new["SelectedLayerVertices"] <= old["SelectedLayerVertices"]:
        raise ValueError("The block-scale cave mesh did not carry more geometry than 2x2")
    if len({sample["Terrain"]["ResourceGeneration"] for sample in (fine_before, coarse, lod)}) != 1:
        raise ValueError("Resource generation changed during the quality comparison")
    for sample in (fine_before, coarse, lod):
        if sample["Coverage"]["HoleCount"] or sample["Coverage"]["OverlapCount"]:
            raise ValueError("Local ownership has holes or overlaps during the comparison")

    def drawn_rows(quality):
        return {(row["ChunkX"], row["ChunkZ"], row["Layer"]): row
                for row in quality["Local"] if row["LayerBodyDrawn"] and
                not row["SpatialOwned"] and row["HasSelectedLayerGeometry"]}

    coarse_rows = drawn_rows(middle)
    fine_rows = drawn_rows(after)
    stable = [(coarse_rows[key], fine_rows[key]) for key in coarse_rows.keys() & fine_rows.keys()
              if coarse_rows[key]["TerrainRevision"] == fine_rows[key]["TerrainRevision"] and
              coarse_rows[key]["UploadedLevels"] == fine_rows[key]["UploadedLevels"]]
    if not stable:
        raise ValueError("No stable drawn local columns to compare")
    changed = sum(old_row["SelectedLevel"] != new_row["SelectedLevel"]
                  for old_row, new_row in stable)
    return (old["SelectedLayerVertices"], new["SelectedLayerVertices"],
            len(stable), changed,
            sum(old_row["SelectedLayerVertices"] for old_row, _ in stable),
            sum(new_row["SelectedLayerVertices"] for _, new_row in stable))


if __name__ == "__main__":
    try:
        coarse_vertices, fine_vertices, stable, changed, coarse_total, fine_total = check(Path(sys.argv[1]))
    except (IndexError, KeyError, OSError, TypeError, ValueError) as error:
        sys.exit(f"Natural-cave handoff: {error}")
    print("Natural-cave comparison captured: same source/camera, L1->L0, "
          f"selected cave-body vertices {coarse_vertices}->{fine_vertices}; "
          f"stable drawn layers {stable}, changed {changed}, "
          f"selected vertices {coarse_total}->{fine_total}")
