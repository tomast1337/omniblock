namespace OmniBlock.Worlds.Lod;

/// <summary>
///     Defines the adaptive source-tile partition required to cover a radial distant-terrain
///     horizon. Coarse tiles cover the interior while boundary tiles descend to the minimum level,
///     avoiding requests for coarse records that extend far outside the configured horizon.
/// </summary>
public static class TerrainLodCoveragePlanner
{
    public static TerrainLodTileKey[] RequiredTiles(
        double cameraChunkX,
        double cameraChunkZ,
        int nearDistanceChunks,
        int horizonDistanceChunks,
        int rootLevel,
        int minimumLevel)
    {
        if (!double.IsFinite(cameraChunkX))
            throw new ArgumentOutOfRangeException(nameof(cameraChunkX));
        if (!double.IsFinite(cameraChunkZ))
            throw new ArgumentOutOfRangeException(nameof(cameraChunkZ));
        if (nearDistanceChunks < 0)
            throw new ArgumentOutOfRangeException(nameof(nearDistanceChunks));
        if (horizonDistanceChunks <= nearDistanceChunks)
            return [];
        if (rootLevel is < 0 or > TerrainLodTileKey.MaximumLevel)
            throw new ArgumentOutOfRangeException(nameof(rootLevel));
        if (minimumLevel < 0 || minimumLevel > rootLevel)
            throw new ArgumentOutOfRangeException(nameof(minimumLevel));

        var width = 1 << rootLevel;
        var minX = (int)Math.Floor((cameraChunkX - horizonDistanceChunks) / width);
        var maxX = (int)Math.Floor((cameraChunkX + horizonDistanceChunks) / width);
        var minZ = (int)Math.Floor((cameraChunkZ - horizonDistanceChunks) / width);
        var maxZ = (int)Math.Floor((cameraChunkZ + horizonDistanceChunks) / width);
        List<TerrainLodTileKey> required = [];
        for (var x = minX; x <= maxX; x++)
        for (var z = minZ; z <= maxZ; z++)
            Visit(new TerrainLodTileKey(rootLevel, x, z));

        return [.. required
            .OrderBy(key => key.DistanceTo(cameraChunkX, cameraChunkZ))
            .ThenByDescending(static key => key.Level)
            .ThenBy(static key => key.X)
            .ThenBy(static key => key.Z)];

        void Visit(TerrainLodTileKey key)
        {
            var nearest = key.DistanceTo(cameraChunkX, cameraChunkZ);
            var furthest = FurthestDistanceTo(key, cameraChunkX, cameraChunkZ);
            if (nearest > horizonDistanceChunks || furthest <= nearDistanceChunks) return;
            var whollyInsideAnnulus = furthest <= horizonDistanceChunks &&
                                      nearest >= nearDistanceChunks;
            if (whollyInsideAnnulus || key.Level == minimumLevel)
            {
                required.Add(key);
                return;
            }
            for (var index = 0; index < 4; index++) Visit(key.Child(index));
        }
    }

    /// <summary>
    ///     A tile is covered by itself or by a complete descendant partition down to the minimum
    ///     remotely presentable level.
    /// </summary>
    public static bool HasCompleteCoverage(
        TerrainLodTileKey root,
        int minimumLevel,
        Func<TerrainLodTileKey, bool> isAvailable)
    {
        ArgumentNullException.ThrowIfNull(isAvailable);
        if (minimumLevel < 0 || minimumLevel > root.Level)
            throw new ArgumentOutOfRangeException(nameof(minimumLevel));
        if (isAvailable(root)) return true;
        if (root.Level == minimumLevel) return false;
        for (var index = 0; index < 4; index++)
            if (!HasCompleteCoverage(root.Child(index), minimumLevel, isAvailable))
                return false;
        return true;
    }

    private static double FurthestDistanceTo(
        TerrainLodTileKey key,
        double chunkX,
        double chunkZ)
    {
        var dx = Math.Max(
            Math.Abs(key.MinChunkX - chunkX),
            Math.Abs(key.MaxChunkX + 1.0 - chunkX));
        var dz = Math.Max(
            Math.Abs(key.MinChunkZ - chunkZ),
            Math.Abs(key.MaxChunkZ + 1.0 - chunkZ));
        return Math.Sqrt(dx * dx + dz * dz);
    }
}
