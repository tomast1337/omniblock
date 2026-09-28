#!/usr/bin/env python3
"""Explicit reference comparison. Never create or bless a baseline automatically.

Requires Pillow. The same viewport/resource pack/backend should be used for both runs.
Artifacts include per-page metrics and amplified PNG diffs, including on failure.
"""
import argparse
import json
from pathlib import Path

from PIL import Image, ImageChops, ImageEnhance, ImageFilter, ImageStat


def load_run(root):
    result = json.loads((root / "result.json").read_text())
    if result.get("exitCode") != 0:
        raise ValueError(f"Unsuccessful gallery run: {root}")
    paths = sorted(root.glob("block-gallery-[0-9][0-9][0-9].json"))
    pages = [json.loads(path.read_text()) for path in paths]
    images = sorted((root / "screenshots").glob("*.png"))
    if not pages or len(images) != len(pages) or len(pages) != pages[0]["pages"]:
        raise ValueError("Incomplete gallery manifests/screenshots")
    samples = []
    for index, page in enumerate(pages):
        if page["schema"] != 1 or page["page"] != index or page["pages"] != len(pages):
            raise ValueError("Invalid gallery page order or schema")
        samples.extend((sample["Id"], sample["Meta"]) for sample in page["samples"])
        with Image.open(images[index]) as image:
            if image.size != (page["viewport"]["width"], page["viewport"]["height"]):
                raise ValueError("Capture viewport differs from manifest")
            if max(ImageStat.Stat(image.convert("RGB")).stddev) < 5:
                raise ValueError("Blank gallery screenshot")
    if len(samples) != pages[0]["sampleCount"] or len(set(samples)) != len(samples):
        raise ValueError("Missing or duplicate block samples")
    if len({sample[0] for sample in samples}) != pages[0]["catalogCount"]:
        raise ValueError("Gallery does not cover the catalog")
    return pages, images


def measure(reference, candidate):
    if reference.size != candidate.size:
        raise ValueError("Different screenshot dimensions")
    # Small raster-edge noise is acceptable; shape/material loss is not. Local windows prevent
    # a single missing block being diluted by all the sky/background in the full-frame average.
    # The wide studio occludes the surrounding world, so no part of the capture needs masking.
    first = reference.convert("RGB").filter(ImageFilter.GaussianBlur(.6))
    second = candidate.convert("RGB").filter(ImageFilter.GaussianBlur(.6))
    diff = ImageChops.difference(first, second)
    data = diff.tobytes()
    pixels = list(zip(data[0::3], data[1::3], data[2::3]))
    width, height = diff.size
    changed = [max(pixel) > 40 for pixel in pixels]
    worst = 0
    for y in range(0, height, 16):
        for x in range(0, width, 16):
            values = [changed[row * width + col]
                      for row in range(y, min(y + 32, height))
                      for col in range(x, min(x + 32, width))]
            worst = max(worst, sum(values) / len(values))
    return {
        "meanRgbError": sum(map(sum, pixels)) / (3 * len(pixels)),
        "changedFraction": sum(changed) / len(changed),
        "worst32pxChangedFraction": worst,
    }, diff


def check(reference_dir, candidate_dir):
    reference_pages, reference_images = load_run(reference_dir)
    pages, images = load_run(candidate_dir)
    if pages != reference_pages:
        raise ValueError("Gallery contract changed (catalog/state/order/viewport/settings/pack); review a new baseline")
    output = candidate_dir / "gallery-diff"
    output.mkdir(exist_ok=True)
    reports = []
    for index, (reference, candidate) in enumerate(zip(reference_images, images)):
        with Image.open(reference) as first, Image.open(candidate) as second:
            metrics, diff = measure(first, second)
            ImageEnhance.Brightness(diff).enhance(4).save(output / f"page-{index:03d}.png")
        # Initial coarse tripwire, not pixel-perfect parity. Animation frames are fixed by the
        # fixture; thresholds must not be enlarged to accommodate model regressions.
        passed = metrics["meanRgbError"] <= 3 and metrics["changedFraction"] <= .02 and \
            metrics["worst32pxChangedFraction"] <= .45
        reports.append({"page": index, "passed": passed, **metrics})
    (output / "report.json").write_text(json.dumps(reports, indent=2) + "\n")
    failed = [report for report in reports if not report["passed"]]
    if failed:
        raise ValueError(f"Visual regression in pages {[report['page'] for report in failed]}; see {output}")
    return reports


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("candidate", type=Path)
    parser.add_argument("--reference", type=Path)
    args = parser.parse_args()
    try:
        if args.reference:
            reports = check(args.reference, args.candidate)
            print(f"Block gallery comparison passed: {len(reports)} pages")
        else:
            pages, _ = load_run(args.candidate)
            print(f"Captured {len(pages)} complete gallery pages; NO visual comparison performed. "
                  "Review these images before using --reference.")
    except (OSError, ValueError, KeyError) as error:
        parser.exit(1, f"Block gallery: {error}\n")
