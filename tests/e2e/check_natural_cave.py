#!/usr/bin/env python3
"""Check the natural-cave quality handoff and record same-camera visual fidelity."""
import json
from pathlib import Path
import sys
import zlib

from check_terrain_lod_visual_baseline import compare, rgb_rows


def cave_crop_difference(first_path, second_path, region=(.38, .59, .38, .60)):
    """Compare the central rock opening, excluding the changing outer streaming horizon."""
    width, height, first = rgb_rows(first_path)
    second_width, second_height, second = rgb_rows(second_path)
    if (width, height) != (second_width, second_height):
        raise ValueError("Natural-cave close-ups have different dimensions")
    x0, x1 = int(width * region[0]), int(width * region[1])
    y0, y1 = int(height * region[2]), int(height * region[3])
    channel_error = strongly_changed = 0
    for y in range(y0, y1):
        for x in range(x0, x1):
            index = x * 3
            difference = [abs(a - b) for a, b in zip(
                first[y][index:index + 3], second[y][index:index + 3])]
            channel_error += sum(difference)
            strongly_changed += max(difference) > 40
    count = (x1 - x0) * (y1 - y0)
    return {
        "region": list(region),
        "meanAbsoluteRgbError": round(channel_error / (3 * count), 3),
        "stronglyChangedFraction": round(strongly_changed / count, 5),
    }


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
    views = [sample.get("View") for sample in (exact, exact_zoom, coarse, lod, lod_zoom)]
    if any(view is None for view in views) and not all(view is None for view in views):
        raise ValueError("Look-direction diagnostics are missing from only some captures")
    if all(view is not None for view in views) and len({
            (view["Yaw"], view["Pitch"]) for view in views}) != 1:
        raise ValueError("Exact and LOD captures used different look directions")
    camera = before["Camera"]
    if camera["X"] != 24 or camera["Z"] != -70 or not 114 < camera["Y"] < 118:
        raise ValueError("The natural-cave comparison camera moved from its fixed fixture")
    if (before["ExactRadiusChunks"], initial["ExactRadiusChunks"],
            middle["ExactRadiusChunks"], after["ExactRadiusChunks"]) != (8, 4, 4, 4):
        raise ValueError("The eight-to-four exact-distance handoff was not presented")
    if any(snapshot["HorizonRadiusChunks"] != 16 for snapshot in (before, initial, middle, after)):
        raise ValueError("Expected the bounded sixteen-chunk horizon")
    if exact["Coverage"]["ExpectedColumns"] == 0 or any(
            exact["Coverage"][key] for key in (
                "HoleCount", "OverlapCount", "MissingChunkDataColumns",
                "MissingExactMeshColumns", "MissingPresentationColumns")):
        raise ValueError("Exact natural-cave reference was captured before coverage completed")
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
    # (24,76,33) belongs to chunk (1,2). Depending on when the remotely compiled L2
    # presentation becomes ready, either the local column or the matching spatial tile may
    # own it. Do not demand a local row after the spatial owner has atomically taken over.
    def cave_column(quality):
        rows = [row for row in quality["Local"] if row["ChunkX"] == 1
                and row["ChunkZ"] == 2 and row["Layer"] == "solid"]
        if len(rows) == 1 and not rows[0]["SpatialOwned"]:
            column = rows[0]
            if not column["LayerBodyDrawn"] or not column["HasSelectedLayerGeometry"] or \
                    column["SelectedLayerVertices"] <= 0 or \
                    column["SelectedLevel"] not in column["UploadedLevels"]:
                raise ValueError("Natural-cave chunk has no drawn local solid layer")
            return {"owner": "local", "row": column}
        tiles = [row for row in quality["Spatial"] if row["Tile"]["Level"] == 2 and
                 row["Tile"]["X"] == 0 and row["Tile"]["Z"] == 0 and row["Authoritative"]]
        if len(tiles) != 1 or tiles[0]["SelectedSolidPages"] <= 0 or \
                tiles[0]["Mesh"]["HorizontalSampleBlocks"] != 1:
            raise ValueError("Natural-cave chunk has no drawn block-scale spatial owner")
        return {"owner": "spatial", "row": tiles[0]}

    first = cave_column(initial)
    old = cave_column(middle)
    new = cave_column(after)
    close = cave_column(zoom_after)
    if len({sample["owner"] for sample in (first, old, new, close)}) != 1:
        raise ValueError("Natural-cave owner changed during the quality comparison")
    if new["owner"] == "local":
        first_row, old_row, new_row, close_row = [sample["row"] for sample in
                                                    (first, old, new, close)]
        if close_row["SelectedLevel"] != 0 or \
                close_row["TerrainRevision"] != new_row["TerrainRevision"]:
            raise ValueError("Natural-opening close-up lost the local 1x1 cave source")
        if len({row["TerrainRevision"] for row in (first_row, old_row, new_row)}) != 1 or \
                any(row["UploadedLevels"] != first_row["UploadedLevels"]
                    for row in (old_row, new_row)):
            raise ValueError("The local quality selections used different source or levels")
        if tuple(row["SelectedLevel"] for row in (first_row, old_row, new_row)) != (0, 1, 0):
            raise ValueError("The local cave did not select 1x1, 2x2, 1x1")
        if new_row["SelectedLayerVertices"] <= old_row["SelectedLayerVertices"]:
            raise ValueError("The block-scale cave mesh did not carry more geometry than 2x2")
        cave_geometry = [old_row["SelectedLayerVertices"], new_row["SelectedLayerVertices"]]
    else:
        hashes = [sample["row"]["PublishedHash"] for sample in (first, old, new, close)]
        if len(set(hashes)) != 1:
            raise ValueError("Natural-cave spatial source changed during the fixed-camera pair")
        cave_geometry = [old["row"]["Mesh"]["SolidQuads"],
                         new["row"]["Mesh"]["SolidQuads"]]
    if len({sample["Terrain"]["ResourceGeneration"] for sample in (fine_before, coarse, lod)}) != 1:
        raise ValueError("Resource generation changed during the quality comparison")
    for sample in (fine_before, coarse, lod):
        coverage = sample["Coverage"]
        if (coverage["HoleCount"] != coverage["MissingChunkDataColumns"] or
                coverage["MissingExactMeshColumns"] or
                coverage["MissingPresentationColumns"] or
                coverage["OverlapCount"]):
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

    screenshots = sorted((artifacts / "screenshots").glob("*.png"))
    if len(screenshots) != 5:
        raise ValueError("Expected exact, exact-zoom, coarse, fine, and fine-zoom screenshots")
    visual = {
        "exactToCoarse": compare(screenshots[0], screenshots[2]),
        "exactToFine": compare(screenshots[0], screenshots[3]),
        "exactToFineZoom": compare(screenshots[1], screenshots[4]),
        "caveCropExactToFineZoom": cave_crop_difference(screenshots[1], screenshots[4]),
        "coarseToFine": compare(screenshots[2], screenshots[3]),
    }
    # These are characterization metrics, not an aesthetic pass gate. The ordinary game-streaming
    # fixture can still gain source terrain between captures, and the generic blue-pixel mask
    # also classifies deep water as sky. A complete-source comparison needs its own fixture.
    measurements = {
        "caveOwner": new["owner"],
        "caveGeometryCount": cave_geometry,
        "exactCoverage": {key: exact["Coverage"][key] for key in (
            "HoleCount", "MissingChunkDataColumns", "MissingExactMeshColumns",
            "MissingPresentationColumns")},
        "stableDrawnLayers": len(stable),
        "changedSelectedLevels": changed,
        "selectedStableVertices": [
            sum(old_row["SelectedLayerVertices"] for old_row, _ in stable),
            sum(new_row["SelectedLayerVertices"] for _, new_row in stable),
        ],
        "visual": visual,
    }
    (artifacts / "natural-cave-quality-metrics.json").write_text(
        json.dumps(measurements, indent=2) + "\n")
    return measurements


if __name__ == "__main__":
    try:
        result = check(Path(sys.argv[1]))
    except (IndexError, KeyError, OSError, TypeError, ValueError, zlib.error) as error:
        sys.exit(f"Natural-cave handoff: {error}")
    print("Natural-cave comparison captured: " + json.dumps(result, sort_keys=True))
