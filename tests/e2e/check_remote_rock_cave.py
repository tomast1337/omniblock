#!/usr/bin/env python3
"""Check the persisted rock-cave LOD/exact ownership and central terrain pair.

The Luau scenario checks actual source and mesh-policy air beneath a stone roof;
this checker verifies the same-camera presentation metadata and a narrow screen
patch where a previous exact-radius handoff exposed sky instead of hillside.
The patch is a regression guard, not a cave-face pixel-fidelity oracle.
"""

import json
from pathlib import Path
import struct
import sys
import zlib


def dimensions(path):
    png = path.read_bytes()
    if len(png) < 24 or png[:8] != b"\x89PNG\r\n\x1a\n" or png[12:16] != b"IHDR":
        raise ValueError(f"Invalid PNG screenshot: {path}")
    return struct.unpack_from(">II", png, 16)


def central_terrain_sky_fraction(path):
    """Fraction of sky-blue pixels in a fixed, overlay-free hillside patch."""
    png = path.read_bytes()
    if png[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError(f"Invalid PNG screenshot: {path}")
    offset = 8
    image_data = bytearray()
    width = height = None
    while offset < len(png):
        length = struct.unpack_from(">I", png, offset)[0]
        kind = png[offset + 4:offset + 8]
        payload = png[offset + 8:offset + 8 + length]
        offset += 12 + length
        if kind == b"IHDR":
            width, height, depth, color, compression, filtering, interlace = struct.unpack(
                ">IIBBBBB", payload)
            if (depth, color, compression, filtering, interlace) != (8, 2, 0, 0, 0):
                raise ValueError(f"Expected an 8-bit RGB screenshot: {path}")
        elif kind == b"IDAT":
            image_data.extend(payload)
        elif kind == b"IEND":
            break
    if width is None or height is None:
        raise ValueError(f"Missing PNG dimensions: {path}")
    x0, x1 = int(width * .44), int(width * .48)
    y0, y1 = int(height * .50), int(height * .56)
    if x1 <= x0 or y1 <= y0:
        raise ValueError(f"Screenshot is too small for central terrain patch: {path}")
    pixels = zlib.decompress(image_data)
    stride = width * 3
    if len(pixels) != height * (stride + 1):
        raise ValueError(f"Invalid screenshot pixel length: {path}")
    previous = bytearray(stride)
    offset = sky = count = 0
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
                row[i] = (row[i] + (left, above, upper_left)[distances.index(min(distances))]) & 255
        if y0 <= y < y1:
            for x in range(x0, x1):
                red, green, blue = row[x * 3:x * 3 + 3]
                sky += blue > red * 1.3 and blue > green * 1.15 and blue > 110
                count += 1
        previous = row
    return sky / count


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
    sky_fractions = [central_terrain_sky_fraction(path) for path in screenshots]
    if any(fraction > .12 for fraction in sky_fractions):
        raise ValueError("Central hillside exposed sky during spatial/exact handoff: " +
                         ", ".join(f"{fraction:.3f}" for fraction in sky_fractions))
    return cave[0]["PublishedHash"], dimensions(screenshots[0]), sky_fractions


if __name__ == "__main__":
    try:
        source_hash, image_size, sky_fractions = check(Path(sys.argv[1]))
    except (IndexError, KeyError, OSError, TypeError, ValueError, zlib.error) as error:
        sys.exit(f"Remote rock cave comparison: {error}")
    print(f"Remote rock cave source/mesh/exact handoff passed; "
          f"source {source_hash[:12]}, image {image_size[0]}x{image_size[1]}, "
          f"sky patch {'/'.join(f'{value:.3f}' for value in sky_fractions)}")
