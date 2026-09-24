namespace OmniBlock.Worlds.Lod;

/// <summary>
///     Defines the adaptive source-tile partition required to cover a radial distant-terrain
///     horizon. Coarse tiles cover the interior while boundary tiles descend to the minimum level,
///     avoiding requests for coarse records that extend far outside the configured horizon.
/// </summary>
public static class TerrainLodCoveragePlanner
{
    /// <summary>
    ///     Builds a coverage partition without ever retaining more than <paramref name="maximumTiles"/>
    ///     nodes. When the requested far-boundary resolution does not fit, only that boundary is
    ///     coarsened; the near exact/LOD handoff floor remains unchanged.
    /// </summary>
    public static TerrainLodCoveragePlan PlanRequiredTiles(
        double cameraChunkX,
        double cameraChunkZ,
        int nearDistanceChunks,
        int horizonDistanceChunks,
        int rootLevel,
        int nearBoundaryMinimumLevel,
        int outerBoundaryMinimumLevel,
        int maximumTiles = TerrainLodScaleBudget.MaximumCoverageTiles)
    {
        if (TryPlanRequiredTiles(
                cameraChunkX, cameraChunkZ, nearDistanceChunks, horizonDistanceChunks,
                rootLevel, nearBoundaryMinimumLevel, outerBoundaryMinimumLevel,
                out var plan, maximumTiles))
            return plan!;
        throw new InvalidOperationException(
            $"Terrain LOD coverage cannot fit within the hard {maximumTiles}-tile budget " +
            $"at root level {rootLevel}. The near-boundary level " +
            $"{nearBoundaryMinimumLevel} would need a coarser quality policy.");
    }

    /// <summary>
    ///     Bounded form used by optional refinement. A false result means the caller should defer
    ///     that refinement tier; no oversized intermediate array is retained.
    /// </summary>
    public static bool TryPlanRequiredTiles(
        double cameraChunkX,
        double cameraChunkZ,
        int nearDistanceChunks,
        int horizonDistanceChunks,
        int rootLevel,
        int nearBoundaryMinimumLevel,
        int outerBoundaryMinimumLevel,
        out TerrainLodCoveragePlan? plan,
        int maximumTiles = TerrainLodScaleBudget.MaximumCoverageTiles)
    {
        if (maximumTiles <= 0) throw new ArgumentOutOfRangeException(nameof(maximumTiles));
        ValidateArguments(
            cameraChunkX, cameraChunkZ, nearDistanceChunks, horizonDistanceChunks,
            rootLevel, nearBoundaryMinimumLevel, outerBoundaryMinimumLevel);
        if (horizonDistanceChunks <= nearDistanceChunks)
        {
            plan = new TerrainLodCoveragePlan(
                [], outerBoundaryMinimumLevel, outerBoundaryMinimumLevel);
            return true;
        }

        for (var effectiveOuterLevel = outerBoundaryMinimumLevel;
             effectiveOuterLevel <= rootLevel;
             effectiveOuterLevel++)
        {
            if (!TryRequiredTiles(
                    cameraChunkX, cameraChunkZ, nearDistanceChunks, horizonDistanceChunks,
                    rootLevel, nearBoundaryMinimumLevel, effectiveOuterLevel,
                    maximumTiles, out var tiles))
                continue;
            plan = new TerrainLodCoveragePlan(
                tiles, outerBoundaryMinimumLevel, effectiveOuterLevel);
            return true;
        }

        plan = null;
        return false;
    }

    public static TerrainLodTileKey[] RequiredTiles(
        double cameraChunkX,
        double cameraChunkZ,
        int nearDistanceChunks,
        int horizonDistanceChunks,
        int rootLevel,
        int minimumLevel) => RequiredTiles(
        cameraChunkX,
        cameraChunkZ,
        nearDistanceChunks,
        horizonDistanceChunks,
        rootLevel,
        minimumLevel,
        minimumLevel);

    /// <summary>
    ///     Builds an adaptive annulus with independent refinement floors at its inner and outer
    ///     boundaries. The exact-terrain handoff can retain fine level-2 coverage while a very
    ///     distant circular edge stops at a coarser level instead of producing O(radius) fine
    ///     records merely to approximate the horizon curve.
    /// </summary>
    public static TerrainLodTileKey[] RequiredTiles(
        double cameraChunkX,
        double cameraChunkZ,
        int nearDistanceChunks,
        int horizonDistanceChunks,
        int rootLevel,
        int nearBoundaryMinimumLevel,
        int outerBoundaryMinimumLevel)
    {
        ValidateArguments(
            cameraChunkX, cameraChunkZ, nearDistanceChunks, horizonDistanceChunks,
            rootLevel, nearBoundaryMinimumLevel, outerBoundaryMinimumLevel);
        _ = TryRequiredTiles(
            cameraChunkX, cameraChunkZ, nearDistanceChunks, horizonDistanceChunks,
            rootLevel, nearBoundaryMinimumLevel, outerBoundaryMinimumLevel,
            int.MaxValue, out var required);
        return required;
    }

    private static bool TryRequiredTiles(
        double cameraChunkX,
        double cameraChunkZ,
        int nearDistanceChunks,
        int horizonDistanceChunks,
        int rootLevel,
        int nearBoundaryMinimumLevel,
        int outerBoundaryMinimumLevel,
        int maximumTiles,
        out TerrainLodTileKey[] tiles)
    {
        if (horizonDistanceChunks <= nearDistanceChunks)
        {
            tiles = [];
            return true;
        }

        List<TerrainLodTileKey> required = new(Math.Min(maximumTiles, 512));
        var exceededBudget = false;
        var width = 1 << rootLevel;
        var minX = (int)Math.Floor((cameraChunkX - horizonDistanceChunks) / width);
        var maxX = (int)Math.Floor((cameraChunkX + horizonDistanceChunks) / width);
        var minZ = (int)Math.Floor((cameraChunkZ - horizonDistanceChunks) / width);
        var maxZ = (int)Math.Floor((cameraChunkZ + horizonDistanceChunks) / width);
        for (var x = minX; x <= maxX && !exceededBudget; x++)
        for (var z = minZ; z <= maxZ && !exceededBudget; z++)
            Visit(new TerrainLodTileKey(rootLevel, x, z));

        if (exceededBudget)
        {
            tiles = [];
            return false;
        }
        tiles = [.. required
            .OrderBy(key => key.DistanceTo(cameraChunkX, cameraChunkZ))
            .ThenByDescending(static key => key.Level)
            .ThenBy(static key => key.X)
            .ThenBy(static key => key.Z)];
        return true;

        void Visit(TerrainLodTileKey key)
        {
            if (exceededBudget) return;
            var nearest = key.DistanceTo(cameraChunkX, cameraChunkZ);
            var furthest = FurthestDistanceTo(key, cameraChunkX, cameraChunkZ);
            if (nearest > horizonDistanceChunks || furthest <= nearDistanceChunks) return;
            var whollyInsideAnnulus = furthest <= horizonDistanceChunks &&
                                      nearest >= nearDistanceChunks;
            var crossesNearBoundary = nearest < nearDistanceChunks &&
                                      furthest > nearDistanceChunks;
            var boundaryMinimumLevel = crossesNearBoundary
                ? nearBoundaryMinimumLevel
                : outerBoundaryMinimumLevel;
            if (whollyInsideAnnulus || key.Level <= boundaryMinimumLevel)
            {
                if (required.Count >= maximumTiles)
                {
                    exceededBudget = true;
                    return;
                }
                required.Add(key);
                return;
            }
            for (var index = 0; index < 4; index++) Visit(key.Child(index));
        }
    }

    private static void ValidateArguments(
        double cameraChunkX,
        double cameraChunkZ,
        int nearDistanceChunks,
        int horizonDistanceChunks,
        int rootLevel,
        int nearBoundaryMinimumLevel,
        int outerBoundaryMinimumLevel)
    {
        if (!double.IsFinite(cameraChunkX))
            throw new ArgumentOutOfRangeException(nameof(cameraChunkX));
        if (!double.IsFinite(cameraChunkZ))
            throw new ArgumentOutOfRangeException(nameof(cameraChunkZ));
        if (nearDistanceChunks < 0)
            throw new ArgumentOutOfRangeException(nameof(nearDistanceChunks));
        if (horizonDistanceChunks <= nearDistanceChunks)
            return;
        if (rootLevel is < 0 or > TerrainLodTileKey.MaximumLevel)
            throw new ArgumentOutOfRangeException(nameof(rootLevel));
        if (nearBoundaryMinimumLevel < 0 || nearBoundaryMinimumLevel > rootLevel)
            throw new ArgumentOutOfRangeException(nameof(nearBoundaryMinimumLevel));
        if (outerBoundaryMinimumLevel < nearBoundaryMinimumLevel ||
            outerBoundaryMinimumLevel > rootLevel)
            throw new ArgumentOutOfRangeException(nameof(outerBoundaryMinimumLevel));
    }

    /// <summary>
    ///     Keeps two refinement octaves below the management root at the far boundary. Thus the
    ///     number of boundary nodes remains approximately stable as the horizon and root level
    ///     grow together, while the inner handoff can retain its independently chosen fine floor.
    /// </summary>
    public static int RecommendedOuterBoundaryMinimumLevel(
        int rootLevel,
        int nearBoundaryMinimumLevel)
    {
        if (rootLevel is < 0 or > TerrainLodTileKey.MaximumLevel)
            throw new ArgumentOutOfRangeException(nameof(rootLevel));
        if (nearBoundaryMinimumLevel < 0 || nearBoundaryMinimumLevel > rootLevel)
            throw new ArgumentOutOfRangeException(nameof(nearBoundaryMinimumLevel));
        return Math.Max(nearBoundaryMinimumLevel, rootLevel - 2);
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
        Func<TerrainLodTileKey, bool> isGpuReady,
        int maximumSelectedNodes = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(requiredRoots);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(isGpuReady);
        if (maximumSelectedNodes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSelectedNodes));

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
        var budgetLimited = false;
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
            var groupRoots = group.Select(static candidate =>
                new TerrainLodCoarseCoverRootSelection(
                    candidate.Root, [.. candidate.Selection.Nodes])).ToArray();
            var projectedNodes = roots.Sum(static root => root.Nodes.Count) +
                                 groupRoots.Sum(static root => root.Nodes.Count);
            if (projectedNodes > maximumSelectedNodes)
            {
                // Prefer a ready management root over its finer descendants. This preserves the
                // band's complete coverage while reducing work instead of truncating arbitrary
                // children. Farthest/largest reductions collapse first.
                foreach (var index in Enumerable.Range(0, groupRoots.Length)
                             .Where(index => groupRoots[index].Nodes.Count > 1 &&
                                             isGpuReady(groupRoots[index].Root))
                             .OrderByDescending(index => groupRoots[index].Nodes.Count - 1)
                             .ThenByDescending(index => group[index].Distance))
                {
                    var root = groupRoots[index].Root;
                    projectedNodes -= groupRoots[index].Nodes.Count - 1;
                    groupRoots[index] = new TerrainLodCoarseCoverRootSelection(
                        root,
                        [new TerrainLodTileSelection(
                            root,
                            policy.HorizontalSampleLevelForSpatialLevel(root.Level),
                            policy.VerticalSliceBudgetForSpatialLevel(root.Level))]);
                    parentFallbacks++;
                    budgetLimited = true;
                    if (projectedNodes <= maximumSelectedNodes) break;
                }
            }
            if (projectedNodes > maximumSelectedNodes)
            {
                // A complete radial band is indivisible. Retain the already complete near prefix
                // rather than allocating or publishing an over-budget partial band.
                completeHorizon = false;
                budgetLimited = true;
                break;
            }
            roots.AddRange(groupRoots);
        }

        return new TerrainLodCoarseCoverSelection(
            [.. roots], roots.Count != 0,
            completeHorizon && roots.Count == candidates.Length,
            parentFallbacks, missingGroups, budgetLimited);
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
        IReadOnlySet<TerrainLodTileKey>? preferredTiles = null,
        int maximumSelectedNodes = int.MaxValue)
    {
        if (maximumSelectedNodes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSelectedNodes));
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

        // Retain a previously displayed island while another is only marginally larger, but
        // never let one early tile pin the entire cold horizon after a substantially larger
        // connected patch becomes ready. Area is measured in chunks because mixed spatial
        // levels can cover very different footprints with the same node count.
        var largestArea = components.Max(ComponentArea);
        var selected = components
            .OrderByDescending(component => ComponentArea(component) * 4 >= largestArea)
            .ThenByDescending(component => PreferredOverlapArea(
                component, preferredTiles))
            .ThenBy(component => component.Min(selection =>
                selection.Tile.DistanceTo(cameraChunkX, cameraChunkZ)))
            .ThenByDescending(ComponentArea)
            .ThenBy(static component => component.Min(selection => selection.Tile.X))
            .ThenBy(static component => component.Min(selection => selection.Tile.Z))
            .First();
        if (selected.Count > maximumSelectedNodes)
            return new TerrainLodCoarseCoverSelection(
                [], false, false,
                forest.Roots.Sum(static root => root.ParentFallbacks),
                forest.Roots.Sum(static root => root.MissingCoverageGroups),
                budgetLimited: true);
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

    private static long ComponentArea(IEnumerable<TerrainLodTileSelection> component) =>
        component.Sum(static selection =>
            (long)selection.Tile.ChunkWidth * selection.Tile.ChunkWidth);

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

public sealed class TerrainLodCoveragePlan
{
    internal TerrainLodCoveragePlan(
        TerrainLodTileKey[] tiles,
        int requestedOuterBoundaryMinimumLevel,
        int effectiveOuterBoundaryMinimumLevel)
    {
        Tiles = Array.AsReadOnly(tiles);
        RequestedOuterBoundaryMinimumLevel = requestedOuterBoundaryMinimumLevel;
        EffectiveOuterBoundaryMinimumLevel = effectiveOuterBoundaryMinimumLevel;
    }

    public IReadOnlyList<TerrainLodTileKey> Tiles { get; }
    public int RequestedOuterBoundaryMinimumLevel { get; }
    public int EffectiveOuterBoundaryMinimumLevel { get; }
    public bool CoarsenedForBudget =>
        EffectiveOuterBoundaryMinimumLevel > RequestedOuterBoundaryMinimumLevel;
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
        int missingCoverageGroups,
        bool budgetLimited = false)
    {
        Roots = Array.AsReadOnly(roots);
        CompleteCoverage = completeCoverage;
        CompleteHorizon = completeHorizon;
        ParentFallbacks = parentFallbacks;
        MissingCoverageGroups = missingCoverageGroups;
        BudgetLimited = budgetLimited;
    }

    public IReadOnlyList<TerrainLodCoarseCoverRootSelection> Roots { get; }
    public bool CompleteCoverage { get; }
    public bool CompleteHorizon { get; }
    public int ParentFallbacks { get; }
    public int MissingCoverageGroups { get; }
    public bool BudgetLimited { get; }
    public int SelectedNodes => Roots.Sum(static root => root.Nodes.Count);
}
