#!/usr/bin/env python3
"""Validate the persisted L2 cave source and compare its exact/spatial presentation."""

import json
from pathlib import Path
import sys
import zlib

from check_natural_cave import cave_crop_difference
from check_terrain_lod_visual_baseline import compare, rgb_rows


CAVE_CROP = (.44, .56, .45, .56)


def cave_dark_mask_difference(first_path, second_path, region=CAVE_CROP):
    width, height, first = rgb_rows(first_path)
    second_width, second_height, second = rgb_rows(second_path)
    if (width, height) != (second_width, second_height):
        raise ValueError("Stable cave screenshots have different dimensions")
    x0, x1 = int(width * region[0]), int(width * region[1])
    y0, y1 = int(height * region[2]), int(height * region[3])
    exact_dark = spatial_dark = disagreement = 0
    for y in range(y0, y1):
        for x in range(x0, x1):
            index = x * 3
            before = max(first[y][index:index + 3]) < 80
            after = max(second[y][index:index + 3]) < 80
            exact_dark += before
            spatial_dark += after
            disagreement += before != after
    count = (x1 - x0) * (y1 - y0)
    return {
        "exactDarkFraction": round(exact_dark / count, 5),
        "spatialDarkFraction": round(spatial_dark / count, 5),
        "disagreementFraction": round(disagreement / count, 5),
    }


def check(artifacts):
    exact = json.loads((artifacts / "terrain-lod-natural-cave-stable-exact.json").read_text())
    spatial = json.loads((artifacts / "terrain-lod-natural-cave-stable-spatial.json").read_text())
    before, after = exact["Quality"], spatial["Quality"]
    if before["Camera"] != after["Camera"] or exact["View"] != spatial["View"] or \
            before["VerticalFov"] != after["VerticalFov"] or \
            before["ViewportHeight"] != after["ViewportHeight"]:
        raise ValueError("Exact and spatial captures changed camera or viewport")
    if (before["ExactRadiusChunks"], after["ExactRadiusChunks"]) != (8, 4) or \
            before["HorizonRadiusChunks"] != 16 or after["HorizonRadiusChunks"] != 16:
        raise ValueError("Stable cave did not perform the eight-to-four-chunk handoff")
    if exact["Terrain"]["ResourceGeneration"] != spatial["Terrain"]["ResourceGeneration"]:
        raise ValueError("Resources changed between stable cave captures")
    for label, dump in (("exact", exact), ("spatial", spatial)):
        coverage = dump["Coverage"]
        if coverage["ExpectedColumns"] == 0 or any(coverage[key] for key in (
                "HoleCount", "OverlapCount", "MissingChunkDataColumns",
                "MissingExactMeshColumns", "MissingPresentationColumns")):
            raise ValueError(f"{label} cave capture has incomplete central coverage: {coverage}")
        if dump["Terrain"]["CacheErrors"]:
            raise ValueError(f"{label} cave capture has a terrain cache error")
    if not spatial["Spatial"]["SubmissionReady"]:
        raise ValueError("Spatial cave presentation was not submitted")
    owners = [row for row in after["Spatial"] if row["Tile"]["Level"] == 2 and
              row["Tile"]["X"] == 0 and row["Tile"]["Z"] == 0 and row["Authoritative"]]
    if len(owners) != 1 or owners[0]["SelectedSolidPages"] <= 0 or \
            owners[0]["Mesh"]["HorizontalSampleBlocks"] != 1 or \
            not owners[0]["CpuSourceMatchesPublishedMesh"]:
        raise ValueError("Cave did not present its current authoritative 1x1 spatial tile")

    screenshots = sorted((artifacts / "screenshots").glob("*.png"))
    if len(screenshots) != 2:
        raise ValueError("Expected exactly two stable cave screenshots")
    width, height, _ = rgb_rows(screenshots[0])
    if width < 640 or height < 360 or \
            abs(exact["View"]["Yaw"]) > .01 or \
            abs(exact["View"]["Pitch"] - 22) > .01 or \
            abs(before["VerticalFov"] - 30) > .01 or \
            abs(before["Camera"]["X"] - 24) > 1 or \
            abs(before["Camera"]["Y"] - 116.62) > 1 or \
            abs(before["Camera"]["Z"] + 70) > 1:
        raise ValueError("Stable cave camera or screenshot size changed")
    metrics = {
        "sourceScope": "persisted exact neighborhood including cave L2 tile (0,0) source chunks; outer view not fully prepared",
        "spatialSourceHash": owners[0]["PublishedHash"],
        "spatialSolidQuads": owners[0]["Mesh"]["SolidQuads"],
        "caveCrop": cave_crop_difference(*screenshots, CAVE_CROP),
        "caveDarkMask": cave_dark_mask_difference(*screenshots),
        "central": compare(*screenshots),
    }
    (artifacts / "natural-cave-stable-metrics.json").write_text(
        json.dumps(metrics, indent=2) + "\n")
    # Two isolated runs of the shipped seed/pack reproduced 17.345 RGB error and 0.1936 dark
    # disagreement in this crop. These loose ceilings catch a gross new cave presentation
    # regression without pretending that the current 1x1 spatial shading matches exact terrain.
    if metrics["caveCrop"]["meanAbsoluteRgbError"] > 24 or \
            metrics["caveCrop"]["stronglyChangedFraction"] > .24 or \
            metrics["caveDarkMask"]["disagreementFraction"] > .28 or \
            metrics["caveDarkMask"]["exactDarkFraction"] < .25 or \
            metrics["caveDarkMask"]["spatialDarkFraction"] < .25:
        raise ValueError(f"Cave crop exceeded its gross-regression ceiling: {metrics}")
    return metrics


if __name__ == "__main__":
    try:
        result = check(Path(sys.argv[1]))
    except (IndexError, KeyError, OSError, TypeError, ValueError, zlib.error) as error:
        sys.exit(f"Stable natural-cave comparison: {error}")
    print("Stable natural-cave comparison: " + json.dumps(result, sort_keys=True))
