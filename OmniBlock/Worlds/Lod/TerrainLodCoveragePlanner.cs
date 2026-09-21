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
    ///     Orders absent coarse-cover nodes by the area they recover for an approximate unit of
    ///     mesh work. Distance is deliberately only the secondary key: completing a large cheap
    ///     part of the horizon is more useful than producing a nearby fine island.
    /// </summary>
    public static TerrainLodTileKey[] PrioritizeMissing(
        IEnumerable<TerrainLodTileKey> keys,
        double cameraChunkX,
        double cameraChunkZ,
        TerrainLodSpatialPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(policy);
        if (!double.IsFinite(cameraChunkX))
            throw new ArgumentOutOfRangeException(nameof(cameraChunkX));
        if (!double.IsFinite(cameraChunkZ))
            throw new ArgumentOutOfRangeException(nameof(cameraChunkZ));

        return [.. keys
            .Distinct()
            .OrderByDescending(key => CoverageGainPerBuildUnit(key, policy))
            .ThenBy(key => key.DistanceTo(cameraChunkX, cameraChunkZ))
            .ThenByDescending(static key => key.Level)
            .ThenBy(static key => key.X)
            .ThenBy(static key => key.Z)];
    }

    /// <summary>
    ///     Selects a distance-closed radial prefix of the required annulus. A frontier band moves
    ///     only when every root in that band is ready; <see cref="TerrainLodCoarseCoverSelection.CompleteHorizon"/>
    ///     separately reports whether the prefix reached the configured horizon.
    /// </summary>
    public static TerrainLodCoarseCoverSelection SelectCompleteCover(
        IEnumerable<TerrainLodTileKey> requiredRoots,
        double cameraChunkX,
        double cameraChunkZ,
        TerrainLodSpatialPolicy policy,
        Func<TerrainLodTileKey, bool> isGpuReady)
    {
        ArgumentNullException.ThrowIfNull(requiredRoots);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(isGpuReady);

        var candidates = requiredRoots
            .Distinct()
            .Select(root => new
            {
                Root = root,
                Distance = root.DistanceTo(cameraChunkX, cameraChunkZ),
                Selection = TerrainLodSpatialSelector.Select(
                    root, cameraChunkX, cameraChunkZ, policy, isGpuReady)
            })
            .OrderBy(static candidate => candidate.Distance)
            .ThenByDescending(static candidate => candidate.Root.Level)
            .ThenBy(static candidate => candidate.Root.X)
            .ThenBy(static candidate => candidate.Root.Z)
            .ToArray();
        List<TerrainLodCoarseCoverRootSelection> roots = [];
        var parentFallbacks = 0;
        var missingGroups = 0;
        var completeHorizon = true;
        var frontierBandWidth = candidates.Length == 0
            ? 1
            : 1 << candidates.Min(static candidate => candidate.Root.Level);
        // A whole minimum-tile-width radial band advances together. Grouping only equal floating
        // point distances would allow angular islands at almost the same radius.
        foreach (var distanceGroup in candidates.GroupBy(candidate =>
                     (int)Math.Floor(candidate.Distance / frontierBandWidth)))
        {
            var group = distanceGroup.ToArray();
            parentFallbacks += group.Sum(static candidate =>
                candidate.Selection.ParentFallbacks);
            missingGroups += group.Sum(static candidate =>
                candidate.Selection.MissingCoverageGroups);
            if (group.Any(static candidate => !candidate.Selection.CompleteCoverage))
            {
                completeHorizon = false;
                break;
            }
            roots.AddRange(group.Select(static candidate =>
                new TerrainLodCoarseCoverRootSelection(
                    candidate.Root, [.. candidate.Selection.Nodes])));
        }

        return new TerrainLodCoarseCoverSelection(
            [.. roots], roots.Count != 0,
            completeHorizon && roots.Count == candidates.Length,
            parentFallbacks, missingGroups);
    }

    /// <summary>
    ///     Selects one connected component from currently owned GPU terrain. This is the cold-cache
    ///     frontier fallback: unavailable source beyond the component remains fogged instead of
    ///     allowing every independently ready island to appear.
    /// </summary>
    public static TerrainLodCoarseCoverSelection SelectContiguousAvailableCover(
        IEnumerable<TerrainLodTileKey> readyKeys,
        int minimumVisibleLevel,
        double cameraChunkX,
        double cameraChunkZ,
        double maximumDistanceChunks,
        TerrainLodSpatialPolicy policy,
        Func<TerrainLodTileKey, bool> isGpuReady,
        IReadOnlySet<TerrainLodTileKey>? preferredTiles = null)
    {
        var forest = TerrainLodSpatialForestSelector.Select(
            readyKeys, minimumVisibleLevel, cameraChunkX, cameraChunkZ,
            maximumDistanceChunks, policy, isGpuReady);
        var nodes = forest.Roots
            .SelectMany(static root => root.Nodes)
            .DistinctBy(static selection => selection.Tile)
            .ToArray();
        if (nodes.Length == 0)
            return new TerrainLodCoarseCoverSelection([], false, false, 0, 0);

        List<List<TerrainLodTileSelection>> components = [];
        HashSet<TerrainLodTileKey> visited = [];
        foreach (var seed in nodes)
        {
            if (!visited.Add(seed.Tile)) continue;
            List<TerrainLodTileSelection> component = [];
            Queue<TerrainLodTileSelection> queue = [];
            queue.Enqueue(seed);
            while (queue.TryDequeue(out var current))
            {
                component.Add(current);
                foreach (var candidate in nodes)
                    if (!visited.Contains(candidate.Tile) &&
                        SharesEdge(current.Tile, candidate.Tile))
                    {
                        visited.Add(candidate.Tile);
                        queue.Enqueue(candidate);
                    }
            }
            components.Add(component);
        }

        var selected = components
            .OrderByDescending(component => PreferredOverlapArea(
                component, preferredTiles))
            .ThenBy(component => component.Min(selection =>
                selection.Tile.DistanceTo(cameraChunkX, cameraChunkZ)))
            .ThenByDescending(static component => component.Sum(selection =>
                (long)selection.Tile.ChunkWidth * selection.Tile.ChunkWidth))
            .ThenBy(static component => component.Min(selection => selection.Tile.X))
            .ThenBy(static component => component.Min(selection => selection.Tile.Z))
            .First();
        return new TerrainLodCoarseCoverSelection(
            [.. selected
                .OrderBy(selection => selection.Tile.DistanceTo(cameraChunkX, cameraChunkZ))
                .ThenByDescending(static selection => selection.Tile.Level)
                .ThenBy(static selection => selection.Tile.X)
                .ThenBy(static selection => selection.Tile.Z)
                .Select(static selection => new TerrainLodCoarseCoverRootSelection(
                    selection.Tile, [selection]))],
            true, false,
            forest.Roots.Sum(static root => root.ParentFallbacks),
            forest.Roots.Sum(static root => root.MissingCoverageGroups));
    }

    private static long PreferredOverlapArea(
        IEnumerable<TerrainLodTileSelection> component,
        IReadOnlySet<TerrainLodTileKey>? preferredTiles)
    {
        if (preferredTiles is null || preferredTiles.Count == 0) return 0;
        long area = 0;
        foreach (var selection in component)
        foreach (var preferred in preferredTiles)
        {
            var width = Math.Min(selection.Tile.MaxChunkX, preferred.MaxChunkX) -
                        Math.Max(selection.Tile.MinChunkX, preferred.MinChunkX) + 1;
            var depth = Math.Min(selection.Tile.MaxChunkZ, preferred.MaxChunkZ) -
                        Math.Max(selection.Tile.MinChunkZ, preferred.MinChunkZ) + 1;
            if (width > 0 && depth > 0) area += checked(width * depth);
        }
        return area;
    }

    public static double CoverageGainPerBuildUnit(
        TerrainLodTileKey key,
        TerrainLodSpatialPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var coveredChunks = (double)key.ChunkWidth * key.ChunkWidth;
        var sampleScale = 1 << policy.HorizontalSampleLevelForSpatialLevel(key.Level);
        var samplesAcross = Math.Max(1, key.ChunkWidth * 16 / sampleScale);
        var buildUnits = (double)samplesAcross * samplesAcross *
                         policy.VerticalSliceBudgetForSpatialLevel(key.Level);
        return coveredChunks / buildUnits;
    }

    /// <summary>
    ///     A recentered frontier may replace the displayed partition only after it owns at least
    ///     as much in-horizon area. Old terrain outside the new horizon does not block progress.
    /// </summary>
    public static bool ShouldRetainPreviousCover(
        IEnumerable<TerrainLodTileSelection> previous,
        IEnumerable<TerrainLodTileSelection> candidate,
        double cameraChunkX,
        double cameraChunkZ,
        double maximumDistanceChunks)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(candidate);
        if (!double.IsFinite(maximumDistanceChunks) || maximumDistanceChunks < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumDistanceChunks));

        var previousArea = previous
            .Where(selection => selection.Tile.DistanceTo(cameraChunkX, cameraChunkZ) <=
                                maximumDistanceChunks)
            .DistinctBy(static selection => selection.Tile)
            .Sum(static selection =>
                (long)selection.Tile.ChunkWidth * selection.Tile.ChunkWidth);
        if (previousArea == 0) return false;
        var candidateArea = candidate
            .DistinctBy(static selection => selection.Tile)
            .Sum(static selection =>
                (long)selection.Tile.ChunkWidth * selection.Tile.ChunkWidth);
        return candidateArea < previousArea;
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

    private static bool SharesEdge(TerrainLodTileKey first, TerrainLodTileKey second)
    {
        var horizontal = (first.MaxChunkX + 1 == second.MinChunkX ||
                          second.MaxChunkX + 1 == first.MinChunkX) &&
                         first.MinChunkZ <= second.MaxChunkZ &&
                         second.MinChunkZ <= first.MaxChunkZ;
        var vertical = (first.MaxChunkZ + 1 == second.MinChunkZ ||
                        second.MaxChunkZ + 1 == first.MinChunkZ) &&
                       first.MinChunkX <= second.MaxChunkX &&
                       second.MinChunkX <= first.MaxChunkX;
        return horizontal || vertical;
    }
}

public sealed class TerrainLodCoarseCoverRootSelection
{
    internal TerrainLodCoarseCoverRootSelection(
        TerrainLodTileKey root,
        TerrainLodTileSelection[] nodes)
    {
        Root = root;
        Nodes = Array.AsReadOnly(nodes);
    }

    public TerrainLodTileKey Root { get; }
    public IReadOnlyList<TerrainLodTileSelection> Nodes { get; }
}

public sealed class TerrainLodCoarseCoverSelection
{
    internal TerrainLodCoarseCoverSelection(
        TerrainLodCoarseCoverRootSelection[] roots,
        bool completeCoverage,
        bool completeHorizon,
        int parentFallbacks,
        int missingCoverageGroups)
    {
        Roots = Array.AsReadOnly(roots);
        CompleteCoverage = completeCoverage;
        CompleteHorizon = completeHorizon;
        ParentFallbacks = parentFallbacks;
        MissingCoverageGroups = missingCoverageGroups;
    }

    public IReadOnlyList<TerrainLodCoarseCoverRootSelection> Roots { get; }
    public bool CompleteCoverage { get; }
    public bool CompleteHorizon { get; }
    public int ParentFallbacks { get; }
    public int MissingCoverageGroups { get; }
    public int SelectedNodes => Roots.Sum(static root => root.Nodes.Count);
}
