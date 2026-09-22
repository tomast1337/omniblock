#!/usr/bin/env python3
"""Require a real server-supplied presentation after applying the smaller exact radius."""
import json
from pathlib import Path
import sys


def check(artifacts):
    before = json.loads((artifacts / "terrain-lod-remote-handoff-before.json").read_text())
    after = json.loads((artifacts / "terrain-lod-remote-handoff-after.json").read_text())
    quality = after["Quality"]
    if before["Quality"]["ExactRadiusChunks"] != 8 or quality["ExactRadiusChunks"] != 4:
        raise ValueError("Distance change was not reflected in the captured render frames")
    if before["Quality"]["Camera"] != quality["Camera"]:
        raise ValueError("Camera moved during the stationary handoff")
    if quality["HorizonRadiusChunks"] != 16:
        raise ValueError("Expected the bounded 16-chunk handoff fixture")
    if quality["CachedForestRevision"] != quality["PresentationRevision"]:
        raise ValueError("Stationary selection did not consume the latest GPU residency revision")
    remote = [tile for tile in quality["Spatial"] or [] if tile["Authoritative"] and
              tile["MatchesRecentRemoteSource"] and tile["SelectedSolidPages"] > 0]
    if not remote or after["Terrain"]["RemoteTiles"] <= 0:
        raise ValueError("No server-received, authoritative spatial tile reached solid submission")
    def owned_columns(snapshot):
        return {(x, z) for row in snapshot["Quality"]["Spatial"] or [] if row["Authoritative"]
                for x in range(row["Tile"]["MinChunkX"], row["Tile"]["MaxChunkX"] + 1)
                for z in range(row["Tile"]["MinChunkZ"], row["Tile"]["MaxChunkZ"] + 1)}

    previous = owned_columns(before)
    current = owned_columns(after)
    if not previous.issubset(current):
        raise ValueError("Previously authoritative outer coverage was lost after shrinking exact distance")
    if not current - previous:
        raise ValueError("No additional spatial tile took authority after shrinking exact distance")
    if after["Coverage"]["HoleCount"] or after["Coverage"]["OverlapCount"]:
        raise ValueError("Local exact/LOD ownership regressed during the handoff")


if __name__ == "__main__":
    try:
        check(Path(sys.argv[1]))
    except (IndexError, KeyError, OSError, ValueError) as error:
        sys.exit(f"Remote handoff regression: {error}")
    print("Remote handoff passed: server tile selected after stationary 8-to-4 radius change")
