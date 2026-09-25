#!/usr/bin/env python3
"""Guard the low-view-distance sky pass without depending on screenshot pixels."""

import csv
import sys
from pathlib import Path


artifacts = Path(sys.argv[1])
profile = artifacts / "profiler-sky-distance4.tsv"
if not profile.is_file():
    raise SystemExit(f"Missing sky profiler dump: {profile}")

with profile.open(newline="") as stream:
    scopes = {row["scope"]: int(row["samples"]) for row in csv.DictReader(stream, delimiter="\t")}

sky_samples = scopes.get("[Main] Render/RenderSky", 0)
if sky_samples <= 0:
    raise SystemExit("The sky pass was not recorded at a four-chunk view distance")

if not any((artifacts / "screenshots").glob("*.png")):
    raise SystemExit("The short-view sky screenshot was not captured")

print(f"Sky pass recorded in {sky_samples} frames at a four-chunk view distance")
