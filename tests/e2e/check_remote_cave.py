#!/usr/bin/env python3
"""Check the persisted natural-cave LOD/exact pair without an image dependency."""

import json
from pathlib import Path
import struct
import sys
import zlib


PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"


def water_band_brightness(path):
    """Mean RGB in a fixed, overlay-free sea patch where a prior run went black."""
    png = path.read_bytes()
    if not png.startswith(PNG_SIGNATURE):
        raise ValueError(f"Not a PNG screenshot: {path}")
    offset = len(PNG_SIGNATURE)
    image_data = bytearray()
    width = height = None
    while offset < len(png):
        length = struct.unpack_from(">I", png, offset)[0]
        kind = png[offset + 4:offset + 8]
        payload = png[offset + 8:offset + 8 + length]
        offset += 12 + length
        if kind == b"IHDR":
            width, height, depth, color, compression, filtering, interlace = struct.unpack(
                ">IIBBBBB", payload
            )
            if (depth, color, compression, filtering, interlace) != (8, 2, 0, 0, 0):
                raise ValueError(f"Expected an 8-bit RGB screenshot: {path}")
        elif kind == b"IDAT":
            image_data.extend(payload)
        elif kind == b"IEND":
            break
    if width is None or height is None or not (0 < width <= 8192 and 0 < height <= 8192):
        raise ValueError(f"Invalid screenshot dimensions: {path}")

    pixels = zlib.decompress(image_data)
    stride = width * 3
    if len(pixels) != height * (stride + 1):
        raise ValueError(f"Invalid screenshot pixel length: {path}")
    x0, x1 = int(width * .67), int(width * .69)
    y0, y1 = int(height * .69), int(height * .715)
    if x1 <= x0 or y1 <= y0:
        raise ValueError(f"Screenshot is too small for the sea patch: {path}")
    previous = bytearray(stride)
    offset = total = count = 0
    for y in range(height):
        filter_kind = pixels[offset]
        row = bytearray(pixels[offset + 1:offset + 1 + stride])
        offset += stride + 1
        if filter_kind not in range(5):
            raise ValueError(f"Unsupported PNG row filter in {path}")
        for i in range(stride):
            left = row[i - 3] if i >= 3 else 0
            above = previous[i]
            upper_left = previous[i - 3] if i >= 3 else 0
            if filter_kind == 1:
                row[i] = (row[i] + left) & 255
            elif filter_kind == 2:
                row[i] = (row[i] + above) & 255
            elif filter_kind == 3:
                row[i] = (row[i] + (left + above) // 2) & 255
            elif filter_kind == 4:
                predictor = left + above - upper_left
                distances = (abs(predictor - left), abs(predictor - above),
                             abs(predictor - upper_left))
                chosen = (left, above, upper_left)[distances.index(min(distances))]
                row[i] = (row[i] + chosen) & 255
        if y0 <= y < y1:
            for x in range(x0, x1):
                pixel = x * 3
                total += row[pixel] + row[pixel + 1] + row[pixel + 2]
                count += 1
        previous = row
    return width, height, total / (count * 3 * 255)


def check(artifacts):
    lod = json.loads((artifacts / "terrain-lod-remote-cave-mouth.json").read_text())
    exact = json.loads((artifacts / "terrain-lod-remote-cave-mouth-exact.json").read_text())
    near, detailed = lod["Quality"], exact["Quality"]
    if near["Camera"] != detailed["Camera"] or near["VerticalFov"] != detailed["VerticalFov"]:
        raise ValueError("LOD and exact screenshots did not use the same camera and FOV")
    if (near["ExactRadiusChunks"], detailed["ExactRadiusChunks"]) != (4, 14):
        raise ValueError("The expected four-to-fourteen exact-radius comparison did not occur")
    if near["HorizonRadiusChunks"] != 16 or detailed["HorizonRadiusChunks"] != 16:
        raise ValueError("The distant horizon changed during comparison")
    if near["CachedForestRevision"] != near["PresentationRevision"]:
        raise ValueError("LOD capture used a stale selection forest")
    cave = [row for row in near["Spatial"] if row["Tile"]["Level"] == 2
            and row["Tile"]["X"] == 8 and row["Tile"]["Z"] == 0]
    if len(cave) != 1 or not cave[0]["Authoritative"] or not cave[0]["CpuSourceMatchesPublishedMesh"]:
        raise ValueError("Natural cave tile was not an authoritative matching L2 presentation")
    if cave[0]["Mesh"]["HorizontalSampleBlocks"] != 1 or cave[0]["SelectedSolidPages"] == 0:
        raise ValueError("Natural cave tile was not selected as visible block-scale geometry")
    for label, sample in (("LOD", lod), ("exact", exact)):
        if sample["Coverage"]["HoleCount"] or sample["Coverage"]["OverlapCount"]:
            raise ValueError(f"{label} capture had a near-field ownership defect")
    if exact["Coverage"]["ExactOwnedColumns"] <= lod["Coverage"]["ExactOwnedColumns"]:
        raise ValueError("Expanding exact distance did not increase exact ownership")

    screenshots = sorted((artifacts / "screenshots").glob("*.png"))
    if len(screenshots) != 2:
        raise ValueError(f"Expected two chronological screenshots, found {len(screenshots)}")
    lod_width, lod_height, lod_brightness = water_band_brightness(screenshots[0])
    exact_width, exact_height, exact_brightness = water_band_brightness(screenshots[1])
    if (lod_width, lod_height) != (exact_width, exact_height):
        raise ValueError("LOD and exact screenshots changed resolution")
    # This is a fixture-specific black-region regression check, not an image-diff fidelity score.
    if exact_brightness < .12 or lod_brightness < .12:
        raise ValueError(f"Sea patch went dark: LOD={lod_brightness:.3f}, exact={exact_brightness:.3f}")
    return lod_brightness, exact_brightness


if __name__ == "__main__":
    try:
        lod_value, exact_value = check(Path(sys.argv[1]))
    except (IndexError, KeyError, OSError, TypeError, ValueError, zlib.error) as error:
        sys.exit(f"Remote cave comparison: {error}")
    print(f"Remote cave LOD/exact comparison passed; sea-patch brightness "
          f"{lod_value:.3f}/{exact_value:.3f}")
