#!/usr/bin/env python3
"""Natural-source exact/LOD comparison; records visual error, not DH parity."""

import json
from pathlib import Path
import struct
import sys
import zlib


def rgb_rows(path):
    png = path.read_bytes()
    if not png.startswith(b"\x89PNG\r\n\x1a\n"):
        raise ValueError(f"Invalid PNG: {path}")
    offset = 8
    image_data = bytearray()
    width = height = None
    while offset < len(png):
        length = struct.unpack_from(">I", png, offset)[0]
        kind = png[offset + 4:offset + 8]
        payload = png[offset + 8:offset + 8 + length]
        offset += length + 12
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
    pixels = zlib.decompress(image_data)
    stride = width * 3
    if len(pixels) != height * (stride + 1):
        raise ValueError(f"Wrong PNG pixel length: {path}")
    rows = []
    previous = bytearray(stride)
    offset = 0
    for _ in range(height):
        filter_kind = pixels[offset]
        row = bytearray(pixels[offset + 1:offset + 1 + stride])
        offset += stride + 1
        if filter_kind not in range(5):
            raise ValueError(f"Unsupported PNG filter {filter_kind}: {path}")
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
        rows.append(row)
        previous = row
    return width, height, rows


def compare(exact, lod):
    width, height, exact_rows = rgb_rows(exact)
    lod_width, lod_height, lod_rows = rgb_rows(lod)
    if (lod_width, lod_height) != (width, height):
        raise ValueError("Exact and LOD screenshots have different dimensions")
    # Center terrain only: no hotbar, chat, hand, sky-only corner, or achievement toast.
    x0, x1 = int(width * .20), int(width * .80)
    y0, y1 = int(height * .15), int(height * .72)
    count = channel_error = strongly_changed = sky_disagreement = 0
    for y in range(y0, y1):
        exact_row, lod_row = exact_rows[y], lod_rows[y]
        for x in range(x0, x1):
            start = x * 3
            first = exact_row[start:start + 3]
            second = lod_row[start:start + 3]
            differences = [abs(a - b) for a, b in zip(first, second)]
            channel_error += sum(differences)
            strongly_changed += max(differences) > 40
            sky = lambda p: p[2] > p[0] * 1.3 and p[2] > p[1] * 1.15 and p[2] > 110
            sky_disagreement += sky(first) != sky(second)
            count += 1
    return {
        "viewport": [width, height],
        "centralMeanAbsoluteRgbError": round(channel_error / (3 * count), 3),
        "centralStronglyChangedFraction": round(strongly_changed / count, 5),
        "centralSkyDisagreementFraction": round(sky_disagreement / count, 5),
    }


def check(artifacts):
    exact = json.loads((artifacts / "terrain-lod-visual-baseline-exact.json").read_text())
    lod = json.loads((artifacts / "terrain-lod-visual-baseline-lod.json").read_text())
    first, second = exact["Quality"], lod["Quality"]
    if first["Camera"] != second["Camera"] or exact["View"] != lod["View"] or \
            first["VerticalFov"] != second["VerticalFov"]:
        raise ValueError("Exact and LOD captures moved the camera or changed FOV")
    if (first["ExactRadiusChunks"], second["ExactRadiusChunks"]) != (8, 4):
        raise ValueError("Fixture did not switch eight exact chunks to four")
    if first["HorizonRadiusChunks"] != 16 or second["HorizonRadiusChunks"] != 16:
        raise ValueError("Horizon changed during comparison")
    for name, sample in (("exact", exact), ("LOD", lod)):
        coverage = sample["Coverage"]
        if coverage["ExpectedColumns"] == 0 or any(coverage[key] for key in (
                "HoleCount", "OverlapCount", "MissingChunkDataColumns",
                "MissingExactMeshColumns", "MissingPresentationColumns")):
            raise ValueError(f"{name} capture has missing source or presentation: {coverage}")
    # The exact-radius footprint is smaller after the switch, so its spatial-owner count need
    # not exceed the wider reference's outer spatial ring. Require non-vacuous spatial ownership.
    if lod["Coverage"]["SpatialOwnedColumns"] == 0:
        raise ValueError("LOD capture has no spatial-owned terrain")
    if lod["Spatial"]["SubmittedSolidPages"] == 0:
        raise ValueError("LOD capture did not draw spatial terrain")
    screenshots = sorted((artifacts / "screenshots").glob("*.png"))
    if len(screenshots) != 2:
        raise ValueError("Expected exactly two exact/LOD screenshots")
    metrics = compare(*screenshots)
    (artifacts / "visual-baseline-metrics.json").write_text(json.dumps(metrics, indent=2) + "\n")
    # Prevent major new sky holes; color fidelity is recorded for improvement, not yet called a pass.
    if metrics["centralSkyDisagreementFraction"] > .01:
        raise ValueError(f"Central terrain silhouette changed into sky: {metrics}")
    return metrics


if __name__ == "__main__":
    try:
        result = check(Path(sys.argv[1]))
    except (IndexError, KeyError, OSError, TypeError, ValueError, zlib.error) as error:
        sys.exit(f"Terrain LOD visual baseline: {error}")
    print("Natural-source exact/LOD baseline: " + json.dumps(result, sort_keys=True))
