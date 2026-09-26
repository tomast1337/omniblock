#!/usr/bin/env python3
"""Validate paired far-color captures from one stable terrain presentation."""

import json
import math
from pathlib import Path
import struct
import sys
import zlib

from check_terrain_lod_visual_baseline import compare, rgb_rows


# Fixed scene-space targets at this camera: ocean across the upper half and forested/sandy land
# below. They deliberately exclude the top-right advancement toast and bottom HUD/hand.
MATERIAL_CROPS = {
    "water": (.20, .80, .24, .37),
    "land": (.32, .72, .52, .70),
}


def is_blue_surface(pixel):
    red, green, blue = pixel
    return blue > red * 1.25 and blue > green * 1.15 and blue > 100


def flat_blue_background_fraction(rows, width, height, region):
    x0, x1 = int(width * region[0]), int(width * region[1])
    y0, y1 = int(height * region[2]), int(height * region[3])
    flat_blue = 0
    for y in range(y0, y1):
        flat_run = 0
        previous = None
        for x in range(x0, x1):
            pixel = rows[y][x * 3:x * 3 + 3]
            blue_surface = is_blue_surface(pixel)
            if blue_surface and previous is not None and \
                    max(abs(a - b) for a, b in zip(pixel, previous)) <= 1:
                flat_run += 1
            else:
                if flat_run >= (x1 - x0) // 5:
                    flat_blue += flat_run
                flat_run = 1 if blue_surface else 0
            previous = pixel
        if flat_run >= (x1 - x0) // 5:
            flat_blue += flat_run
    return round(flat_blue / ((x1 - x0) * (y1 - y0)), 5)


def presented_authoritative_tiles(dump):
    return sorted((entry["Tile"]["Level"], entry["Tile"]["X"],
                   entry["Tile"]["Z"], entry["PublishedHash"])
                  for entry in dump["Quality"]["Spatial"]
                  if entry["Authoritative"] and
                  (entry["SelectedSolidPages"] or entry["SelectedTranslucentPages"]))


def source_footprint(camera, vertical_fov, view, width, height):
    """Bound the entire downward image on the generated Y=0..128 terrain circle."""
    if abs(view["Yaw"] - 180) > .01 or abs(view["Pitch"] - 65) > .01 or \
            abs(vertical_fov - 30) > .01 or \
            abs(camera["X"] - 576) > 1 or abs(camera["Z"] - 752) > 1:
        raise ValueError("Camera no longer matches the source-footprint fixture")
    pitch = math.radians(view["Pitch"])
    tangent = math.tan(math.radians(vertical_fov) / 2)
    aspect = width / height
    maximum = 0
    # Sample the complete image footprint, not just the crop used for pixel statistics. The
    # pregenerator's circle is defined in chunk coordinates, so also test its discrete cells.
    for image_x in range(-8, 9):
        for image_y in range(-8, 9):
            horizontal = image_x / 8 * aspect * tangent
            vertical = image_y / 8 * tangent
            direction_y = -math.sin(pitch) + vertical * math.cos(pitch)
            direction_z = -math.cos(pitch) + vertical * math.sin(pitch)
            if direction_y >= 0:
                raise ValueError("Quality camera no longer looks fully down at terrain")
            for surface_y in (0, 128):
                distance = (camera["Y"] - surface_y) / -direction_y
                world_x = camera["X"] + horizontal * distance
                world_z = camera["Z"] + direction_z * distance
                maximum = max(maximum, math.hypot(world_x - 576, world_z - 576))
                chunk_x = math.floor(world_x / 16) - 36
                chunk_z = math.floor(world_z / 16) - 36
                if chunk_x * chunk_x + chunk_z * chunk_z > 16 * 16:
                    raise ValueError("Image footprint reaches an ungenerated source chunk")
    # The script pregenerates a radius-16 circle: 256 blocks. Keep one block of margin so the
    # image cannot touch a partially populated chunk on the circle's discrete boundary.
    if maximum >= 255:
        raise ValueError(f"Camera projects beyond generated terrain: {maximum:.1f} blocks")
    return round(maximum, 1)


def material_differences(off_path, on_path):
    width, height, before = rgb_rows(off_path)
    _, _, after = rgb_rows(on_path)
    x0, x1 = int(width * .20), int(width * .80)
    y0, y1 = int(height * .15), int(height * .72)
    counts = {"land": 0, "water": 0}
    errors = {"land": 0., "water": 0.}
    changed = {"land": 0, "water": 0}
    for y in range(y0, y1):
        for x in range(x0, x1):
            index = x * 3
            first = before[y][index:index + 3]
            second = after[y][index:index + 3]
            blue_surface = is_blue_surface(first)
            # This is a diagnostic color mask, not a material-ID readback. The fully generated
            # downward footprint and flat-blue gate below keep missing sky out of the water mask.
            if max(first) < 40 or max(first) > 245:
                continue
            material = "water" if blue_surface else "land"
            difference = sum(abs(a - b) for a, b in zip(first, second)) / 3
            counts[material] += 1
            errors[material] += difference
            changed[material] += difference > 2
    area = (x1 - x0) * (y1 - y0)
    if counts["land"] == 0 or counts["water"] == 0:
        raise ValueError("Paired view does not contain both land and water color masks")
    return {
        "centralLandPixels": counts["land"],
        "centralLandFraction": round(counts["land"] / area, 5),
        "centralLandMeanAbsoluteRgbError": round(errors["land"] / counts["land"], 3),
        "centralLandChangedFraction": round(changed["land"] / counts["land"], 5),
        "centralWaterPixels": counts["water"],
        "centralWaterFraction": round(counts["water"] / area, 5),
        "centralWaterMeanAbsoluteRgbError": round(errors["water"] / counts["water"], 3),
        "centralWaterChangedFraction": round(changed["water"] / counts["water"], 5),
        "centralFlatBlueBackgroundFraction": flat_blue_background_fraction(
            before, width, height, (.20, .80, .15, .72)),
        "broadFlatBlueBackgroundFraction": flat_blue_background_fraction(
            before, width, height, (.05, .95, .10, .80)),
    }


def write_png_crop(path, rows, width, height, region):
    x0, x1 = int(width * region[0]), int(width * region[1])
    y0, y1 = int(height * region[2]), int(height * region[3])
    raw = b"".join(b"\0" + bytes(row[x0 * 3:x1 * 3]) for row in rows[y0:y1])

    def chunk(kind, payload):
        return (struct.pack(">I", len(payload)) + kind + payload +
                struct.pack(">I", zlib.crc32(kind + payload)))

    path.write_bytes(b"\x89PNG\r\n\x1a\n" +
                     chunk(b"IHDR", struct.pack(">IIBBBBB", x1 - x0, y1 - y0,
                                                  8, 2, 0, 0, 0)) +
                     chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))


def save_material_crops(artifacts, screenshots):
    width, height, before = rgb_rows(screenshots[0])
    _, _, after = rgb_rows(screenshots[1])
    directory = artifacts / "material-crops"
    directory.mkdir(exist_ok=True)
    metrics = {}
    for material, region in MATERIAL_CROPS.items():
        write_png_crop(directory / f"{material}-off.png", before, width, height, region)
        write_png_crop(directory / f"{material}-on.png", after, width, height, region)
        x0, x1 = int(width * region[0]), int(width * region[1])
        y0, y1 = int(height * region[2]), int(height * region[3])
        count = matching = error = 0
        for y in range(y0, y1):
            for x in range(x0, x1):
                pixel = before[y][x * 3:x * 3 + 3]
                matching += is_blue_surface(pixel) == (material == "water")
                error += sum(abs(a - b) for a, b in zip(
                    pixel, after[y][x * 3:x * 3 + 3]))
                count += 1
        metrics[f"{material}CropMaterialFraction"] = round(matching / count, 5)
        metrics[f"{material}CropMeanAbsoluteRgbError"] = round(error / (3 * count), 3)
    return metrics


def check(artifacts):
    off = json.loads((artifacts / "terrain-lod-filtered-textures-color-off.json").read_text())
    on = json.loads((artifacts / "terrain-lod-filtered-textures-color-on.json").read_text())
    first, second = off["Quality"], on["Quality"]
    if (first["Camera"], first["VerticalFov"], off["View"]) != \
            (second["Camera"], second["VerticalFov"], on["View"]):
        raise ValueError("Camera, field of view or view direction changed between captures")
    if first["ViewportHeight"] != second["ViewportHeight"]:
        raise ValueError("Viewport height changed between captures")
    selected = presented_authoritative_tiles(off)
    if not selected or selected != presented_authoritative_tiles(on):
        raise ValueError("Drawn authoritative terrain tiles changed between captures")
    if max(level for level, *_ in selected) < 3:
        raise ValueError("The representative-color fixture did not present L3 terrain")
    for name, dump in (("off", off), ("on", on)):
        if dump["Terrain"]["CacheErrors"] or not dump["Spatial"]["SubmissionReady"]:
            raise ValueError(f"{name} capture has a cache or spatial-presentation error")
        coverage = dump["Coverage"]
        if coverage["ExpectedColumns"] == 0 or any(coverage[key] for key in (
                "HoleCount", "OverlapCount", "MissingChunkDataColumns",
                "MissingExactMeshColumns", "MissingPresentationColumns")):
            raise ValueError(f"{name} capture has incomplete central presentation: {coverage}")

    screenshots = sorted((artifacts / "screenshots").glob("*.png"))
    if len(screenshots) != 2:
        raise ValueError("Expected exactly two paired screenshots")
    metrics = compare(*screenshots)
    metrics["sourceFootprintRadiusBlocks"] = source_footprint(
        first["Camera"], first["VerticalFov"], off["View"], *metrics["viewport"])
    metrics.update(material_differences(*screenshots))
    metrics.update(save_material_crops(artifacts, screenshots))
    metrics["presentedAuthoritativeTiles"] = len(selected)
    metrics["highestAuthoritativeLevel"] = max(level for level, *_ in selected)
    (artifacts / "color-comparison-metrics.json").write_text(
        json.dumps(metrics, indent=2) + "\n")
    if metrics["centralSkyDisagreementFraction"] > .005:
        raise ValueError(f"Sky/terrain coverage changed between captures: {metrics}")
    if metrics["centralLandFraction"] < .1 or metrics["centralWaterFraction"] < .05:
        raise ValueError(f"Material-specific view lost land or water coverage: {metrics}")
    if metrics["waterCropMaterialFraction"] < .8 or metrics["landCropMaterialFraction"] < .7:
        raise ValueError(f"Fixed material crop no longer shows its intended surface: {metrics}")
    if metrics["centralFlatBlueBackgroundFraction"] > .01:
        raise ValueError(f"Flat blue source/presentation hole in quality view: {metrics}")
    if metrics["broadFlatBlueBackgroundFraction"] > .01:
        raise ValueError(f"Flat blue source/presentation hole beyond central crop: {metrics}")
    return metrics


if __name__ == "__main__":
    try:
        result = check(Path(sys.argv[1]))
    except (IndexError, KeyError, OSError, TypeError, ValueError) as error:
        sys.exit(f"Terrain LOD color comparison: {error}")
    print("Paired terrain LOD color captures: " + json.dumps(result, sort_keys=True))
