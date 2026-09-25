#!/usr/bin/env python3
"""Check the persisted natural-cave pair and report its publication timeline."""

import json
from pathlib import Path
import struct
import sys
import zlib


PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
CAPTURES = (
    "remote-cave-arrival",
    "remote-cave-source-ready",
    "remote-cave-first-spatial",
    "remote-cave-mouth",
    "remote-cave-exact-request",
    "remote-cave-first-exact",
    "remote-cave-mouth-exact",
)


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
    # The old black wedge starts at this sea patch. Keep it right of the
    # temporary chat overlay and above the player's hand in every capture.
    x0, x1 = int(width * .84), int(width * .86)
    y0, y1 = int(height * .69), int(height * .705)
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
    captures = [json.loads((artifacts / f"terrain-lod-{label}.json").read_text())
                for label in CAPTURES]
    lod, exact = captures[3], captures[6]
    near, detailed = lod["Quality"], exact["Quality"]
    if (near["Camera"] != detailed["Camera"] or
            lod["View"] != exact["View"] or
            near["VerticalFov"] != detailed["VerticalFov"]):
        raise ValueError("LOD and exact screenshots did not use the same camera, view and FOV")
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
        coverage = sample["Coverage"]
        if (coverage["HoleCount"] != coverage["MissingChunkDataColumns"] or
                coverage["MissingExactMeshColumns"] or
                coverage["MissingPresentationColumns"] or
                coverage["OverlapCount"]):
            raise ValueError(f"{label} capture had a near-field ownership defect")
    if exact["Coverage"]["ExactOwnedColumns"] <= lod["Coverage"]["ExactOwnedColumns"]:
        raise ValueError("Expanding exact distance did not increase exact ownership")

    screenshots = sorted((artifacts / "screenshots").glob("*.png"))
    if len(screenshots) != len(CAPTURES):
        raise ValueError(f"Expected {len(CAPTURES)} chronological screenshots, "
                         f"found {len(screenshots)}")
    temporal = []
    dimensions = None
    for label, sample, screenshot in zip(CAPTURES, captures, screenshots, strict=True):
        quality = sample["Quality"]
        if (quality["Camera"] != near["Camera"] or sample["View"] != lod["View"] or
                quality["VerticalFov"] != near["VerticalFov"]):
            raise ValueError(f"{label} moved the comparison camera, view or FOV")
        expected_radius = 4 if len(temporal) < 4 else 14
        if quality["ExactRadiusChunks"] != expected_radius:
            raise ValueError(f"{label} did not apply exact radius {expected_radius}")
        width, height, brightness = water_band_brightness(screenshot)
        if dimensions is not None and dimensions != (width, height):
            raise ValueError(f"{label} changed screenshot resolution")
        dimensions = (width, height)
        cave_tiles = [row for row in quality["Spatial"] or [] if row["Tile"]["Level"] == 2
                      and row["Tile"]["X"] == 8 and row["Tile"]["Z"] == 0]
        cave_tile = cave_tiles[0] if len(cave_tiles) == 1 else None
        temporal.append({
            "label": label,
            "screenshot": screenshot.name,
            "seaPatchBrightness": round(brightness, 4),
            "nearOwnershipHoles": sample["Coverage"]["HoleCount"],
            "nearOwnershipOverlaps": sample["Coverage"]["OverlapCount"],
            "remoteMissingSourceGroups": sample["Terrain"]["CoarseCoverSourceUnavailable"],
            "remoteGpuPendingGroups": sample["Terrain"]["CoarseCoverGpuPending"],
            "spatialMissingCoverageGroups": sample["Spatial"]["MissingCoverageGroups"],
            "caveTileAuthoritative": bool(cave_tile and cave_tile["Authoritative"]),
            "caveTileSelectedSolidPages": cave_tile["SelectedSolidPages"] if cave_tile else 0,
            "presentationRevision": quality["PresentationRevision"],
            "selectionRevision": quality["CachedForestRevision"],
        })
    lod_brightness = temporal[3]["seaPatchBrightness"]
    exact_brightness = temporal[6]["seaPatchBrightness"]
    # These are correlated frame-level observations, not pixel-to-tile ownership claims.
    (artifacts / "remote-cave-publication-timeline.json").write_text(
        json.dumps(temporal, indent=2) + "\n")
    # This is a fixture-specific black-region regression check, not an image-diff fidelity score.
    for index in (2, 3, 5, 6):
        if temporal[index]["seaPatchBrightness"] < .12:
            raise ValueError(f"Sea patch went dark at {temporal[index]['label']}: "
                             f"{temporal[index]['seaPatchBrightness']:.3f}")
    return lod_brightness, exact_brightness, temporal


if __name__ == "__main__":
    try:
        lod_value, exact_value, temporal = check(Path(sys.argv[1]))
    except (IndexError, KeyError, OSError, TypeError, ValueError, zlib.error) as error:
        sys.exit(f"Remote cave comparison: {error}")
    print(f"Remote cave LOD/exact comparison passed; sea-patch brightness "
          f"{lod_value:.3f}/{exact_value:.3f}")
    print("Remote cave publication timeline: " + ", ".join(
        f"{row['label']}={row['seaPatchBrightness']:.3f}"
        for row in temporal))
