using System.Collections.ObjectModel;

namespace OmniBlock.Worlds.Lod;

/// <summary>
///     Identifies one node in the distant-terrain spatial hierarchy. Coordinates are expressed in
///     tiles at <see cref="Level"/>: level zero covers one chunk, level one covers 2x2 chunks,
///     level two covers 4x4 chunks, and so on.
/// </summary>
/// <remarks>
///     Spatial level is deliberately independent from horizontal sample level. A 4x4-chunk tile
///     can, for example, retain one-block X/Z samples while a more distant tile with the same
///     footprint is rebuilt with two-block samples by a future quality policy. Vertical slice
///     reduction is a third, separate policy.
/// </remarks>
public readonly record struct TerrainLodTileKey
{
    public const int MaximumLevel = 30;

    public TerrainLodTileKey(int level, int x, int z)
    {
        if (level is < 0 or > MaximumLevel)
            throw new ArgumentOutOfRangeException(nameof(level));
        Level = level;
        X = x;
        Z = z;
    }

    public int Level { get; }
    public int X { get; }
    public int Z { get; }
    public int ChunkWidth => 1 << Level;
    public long MinChunkX => (long)X * ChunkWidth;
    public long MinChunkZ => (long)Z * ChunkWidth;
    public long MaxChunkX => MinChunkX + ChunkWidth - 1L;
    public long MaxChunkZ => MinChunkZ + ChunkWidth - 1L;

    public static TerrainLodTileKey ContainingChunk(int level, int chunkX, int chunkZ)
    {
        if (level is < 0 or > MaximumLevel)
            throw new ArgumentOutOfRangeException(nameof(level));
        var width = 1 << level;
        return new TerrainLodTileKey(
            level,
            FloorDivide(chunkX, width),
            FloorDivide(chunkZ, width));
    }

    public TerrainLodTileKey Parent()
    {
        if (Level == MaximumLevel)
            throw new InvalidOperationException("The maximum terrain LOD tile has no parent.");
        return new TerrainLodTileKey(
            Level + 1,
            FloorDivide(X, 2),
            FloorDivide(Z, 2));
    }

    public TerrainLodTileKey Child(int index)
    {
        if (Level == 0)
            throw new InvalidOperationException("A level-zero terrain LOD tile has no children.");
        if ((uint)index >= 4)
            throw new ArgumentOutOfRangeException(nameof(index));
        return new TerrainLodTileKey(
            Level - 1,
            checked(X * 2 + (index & 1)),
            checked(Z * 2 + ((index >> 1) & 1)));
    }

    public bool ContainsChunk(int chunkX, int chunkZ) =>
        chunkX >= MinChunkX && chunkX <= MaxChunkX &&
        chunkZ >= MinChunkZ && chunkZ <= MaxChunkZ;

    /// <summary>Minimum horizontal distance from a point in chunk coordinates to this tile.</summary>
    public double DistanceTo(double chunkX, double chunkZ)
    {
        if (!double.IsFinite(chunkX)) throw new ArgumentOutOfRangeException(nameof(chunkX));
        if (!double.IsFinite(chunkZ)) throw new ArgumentOutOfRangeException(nameof(chunkZ));
        // Tile bounds are half-open. Using the nearest boundary instead of the center avoids the
        // corner under-refinement that DH compensates for by accepting expectedLevel - 1.
        var dx = chunkX < MinChunkX
            ? MinChunkX - chunkX
            : chunkX > MaxChunkX + 1.0
                ? chunkX - (MaxChunkX + 1.0)
                : 0;
        var dz = chunkZ < MinChunkZ
            ? MinChunkZ - chunkZ
            : chunkZ > MaxChunkZ + 1.0
                ? chunkZ - (MaxChunkZ + 1.0)
                : 0;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    private static int FloorDivide(int value, int divisor)
    {
        var quotient = value / divisor;
        return value % divisor < 0 ? quotient - 1 : quotient;
    }
}

/// <summary>
///     Controls where spatial refinement occurs and which horizontal sample level a spatial tile
///     should use. Repeated sample levels are intentional: spatial footprint and sample density
///     are separate quality axes. A parallel slice budget bounds vertical column complexity
///     without forcing it to halve whenever horizontal detail changes.
/// </summary>
public sealed class TerrainLodSpatialPolicy
{
    /// <summary>
    ///     Version of the canonical spatial sampling and vertical-slice schedules. Increment this
    ///     when equal source terrain can compile into different column tiles under a new policy.
    /// </summary>
    public const int CurrentQualityPolicyVersion = 1;
    public const int MinimumSupportedHorizonChunks = 16;
    /// <summary>Largest horizon currently exposed and accepted by client/server transport.</summary>
    public const int MaximumSupportedHorizonChunks = 256;
    /// <summary>
    ///     Largest dormant policy shape the hierarchy and disk cache can construct. Distances
    ///     above <see cref="MaximumSupportedHorizonChunks"/> remain unavailable to users and peers
    ///     until their Phase 2B scale gates pass.
    /// </summary>
    public const int MaximumGeneratedHorizonChunks = 4096;
    public const int MinimumRemoteSpatialLevel = 2;
    public const int MaximumSupportedSpatialLevel = 6;
    public const int MaximumGeneratedSpatialLevel = 10;
    private const double DefaultDistanceUnitChunks = 4;
    private const double DefaultDistanceGrowth = 2;
    private readonly int[] _horizontalSampleLevelBySpatialLevel;
    private readonly int[] _verticalSliceBudgetBySpatialLevel;

    /// <summary>
    ///     The shared persisted/presented hierarchy contract. Server construction and client
    ///     selection must use the same shape: changing one side alone produces cache records the
    ///     other peer cannot refine consistently.
    /// </summary>
    public static TerrainLodSpatialPolicy CreateDefault() =>
        CreateForMaximumHorizon(MaximumSupportedHorizonChunks);

    /// <summary>
    ///     Builds only the hierarchy depth required by a selected maximum horizon. Spatial
    ///     footprint, horizontal sample density, and vertical slice quality intentionally use
    ///     separate schedules: extending the quadtree must not imply that every quality axis
    ///     loses one octave at the same time.
    /// </summary>
    public static TerrainLodSpatialPolicy CreateForMaximumHorizon(int horizonChunks)
    {
        if (horizonChunks is < MinimumSupportedHorizonChunks or > MaximumGeneratedHorizonChunks)
            throw new ArgumentOutOfRangeException(nameof(horizonChunks),
                $"Terrain LOD horizon must be between {MinimumSupportedHorizonChunks} and " +
                $"{MaximumGeneratedHorizonChunks} chunks.");
        var maximumLevel = RequiredMaximumSpatialLevel(horizonChunks);
        return new TerrainLodSpatialPolicy(
            DefaultDistanceUnitChunks,
            DefaultDistanceGrowth,
            Enumerable.Range(0, maximumLevel + 1)
                .Select(HorizontalSampleLevelForGeneratedSpatialLevel),
            Enumerable.Range(0, maximumLevel + 1)
                .Select(VerticalSliceBudgetForGeneratedSpatialLevel));
    }

    public static int RequiredMaximumSpatialLevel(int horizonChunks)
    {
        if (horizonChunks <= 0)
            throw new ArgumentOutOfRangeException(nameof(horizonChunks));
        var level = horizonChunks <= DefaultDistanceUnitChunks
            ? 0
            : (int)Math.Floor(Math.Log(
                horizonChunks / DefaultDistanceUnitChunks,
                DefaultDistanceGrowth));
        return Math.Clamp(level, 0, MaximumGeneratedSpatialLevel);
    }

    public static int MaximumHorizonChunksForSpatialLevel(int spatialLevel)
    {
        if (spatialLevel is < 0 or > MaximumGeneratedSpatialLevel)
            throw new ArgumentOutOfRangeException(nameof(spatialLevel));
        return checked((int)(DefaultDistanceUnitChunks * (1 << spatialLevel)));
    }

    private static int HorizontalSampleLevelForGeneratedSpatialLevel(int spatialLevel) =>
        spatialLevel <= 4 ? spatialLevel / 2 : spatialLevel - 2;

    private static int VerticalSliceBudgetForGeneratedSpatialLevel(int spatialLevel) =>
        spatialLevel switch
        {
            0 => 32,
            1 => 24,
            2 => 16,
            3 => 12,
            _ => Math.Max(4, 8 - (spatialLevel - 4) * 2)
        };

    public TerrainLodSpatialPolicy(
        double distanceUnitChunks,
        double distanceGrowth,
        IEnumerable<int> horizontalSampleLevelBySpatialLevel,
        IEnumerable<int>? verticalSliceBudgetBySpatialLevel = null)
    {
        if (!double.IsFinite(distanceUnitChunks) || distanceUnitChunks <= 0)
            throw new ArgumentOutOfRangeException(nameof(distanceUnitChunks));
        if (!double.IsFinite(distanceGrowth) || distanceGrowth <= 1)
            throw new ArgumentOutOfRangeException(nameof(distanceGrowth));
        ArgumentNullException.ThrowIfNull(horizontalSampleLevelBySpatialLevel);
        _horizontalSampleLevelBySpatialLevel = horizontalSampleLevelBySpatialLevel.ToArray();
        if (_horizontalSampleLevelBySpatialLevel.Length == 0 ||
            _horizontalSampleLevelBySpatialLevel.Length > TerrainLodTileKey.MaximumLevel + 1)
            throw new ArgumentException(
                $"A terrain LOD policy needs between 1 and {TerrainLodTileKey.MaximumLevel + 1} levels.",
                nameof(horizontalSampleLevelBySpatialLevel));
        for (var i = 0; i < _horizontalSampleLevelBySpatialLevel.Length; i++)
        {
            if (_horizontalSampleLevelBySpatialLevel[i] < 0)
                throw new ArgumentException("Horizontal sample levels cannot be negative.",
                    nameof(horizontalSampleLevelBySpatialLevel));
            if (i > 0 && _horizontalSampleLevelBySpatialLevel[i] <
                _horizontalSampleLevelBySpatialLevel[i - 1])
                throw new ArgumentException(
                    "Horizontal samples must stay equal or become coarser as spatial level increases.",
                    nameof(horizontalSampleLevelBySpatialLevel));
            if (i > 0 && _horizontalSampleLevelBySpatialLevel[i] >
                _horizontalSampleLevelBySpatialLevel[i - 1] + 1)
                throw new ArgumentException(
                    "Incremental parent construction supports at most one horizontal octave per spatial level.",
                    nameof(horizontalSampleLevelBySpatialLevel));
            if (_horizontalSampleLevelBySpatialLevel[i] > i + 4)
                throw new ArgumentException(
                    $"Spatial level {i} cannot represent horizontal sample level " +
                    $"{_horizontalSampleLevelBySpatialLevel[i]} within its chunk footprint.",
                    nameof(horizontalSampleLevelBySpatialLevel));
        }

        DistanceUnitChunks = distanceUnitChunks;
        DistanceGrowth = distanceGrowth;
        HorizontalSampleLevelBySpatialLevel =
            Array.AsReadOnly(_horizontalSampleLevelBySpatialLevel);
        _verticalSliceBudgetBySpatialLevel = verticalSliceBudgetBySpatialLevel?.ToArray() ??
                                             Enumerable.Repeat(
                                                     int.MaxValue,
                                                     _horizontalSampleLevelBySpatialLevel.Length)
                                                 .ToArray();
        if (_verticalSliceBudgetBySpatialLevel.Length !=
            _horizontalSampleLevelBySpatialLevel.Length ||
            _verticalSliceBudgetBySpatialLevel.Any(static value => value <= 0))
            throw new ArgumentException(
                "Vertical slice budgets must be positive and match the number of spatial levels.",
                nameof(verticalSliceBudgetBySpatialLevel));
        for (var i = 1; i < _verticalSliceBudgetBySpatialLevel.Length; i++)
            if (_verticalSliceBudgetBySpatialLevel[i] > _verticalSliceBudgetBySpatialLevel[i - 1])
                throw new ArgumentException(
                    "Vertical slice budgets cannot increase as spatial level becomes coarser.",
                    nameof(verticalSliceBudgetBySpatialLevel));
        VerticalSliceBudgetBySpatialLevel =
            Array.AsReadOnly(_verticalSliceBudgetBySpatialLevel);
    }

    public double DistanceUnitChunks { get; }
    public double DistanceGrowth { get; }
    public int MaximumSpatialLevel => _horizontalSampleLevelBySpatialLevel.Length - 1;
    public ReadOnlyCollection<int> HorizontalSampleLevelBySpatialLevel { get; }
    public ReadOnlyCollection<int> VerticalSliceBudgetBySpatialLevel { get; }

    /// <summary>
    ///     Returns the desired spatial level using the same logarithmic shape as Distant Horizons:
    ///     floor(log(distance / unit) / log(growth)), clamped to the configured hierarchy.
    /// </summary>
    public int DesiredSpatialLevel(double distanceChunks)
    {
        if (!double.IsFinite(distanceChunks) || distanceChunks < 0)
            throw new ArgumentOutOfRangeException(nameof(distanceChunks));
        if (distanceChunks <= DistanceUnitChunks) return 0;
        var level = (int)Math.Floor(
            Math.Log(distanceChunks / DistanceUnitChunks) / Math.Log(DistanceGrowth));
        return Math.Clamp(level, 0, MaximumSpatialLevel);
    }

    public int HorizontalSampleLevelForSpatialLevel(int spatialLevel)
    {
        if (spatialLevel < 0) throw new ArgumentOutOfRangeException(nameof(spatialLevel));
        return _horizontalSampleLevelBySpatialLevel[
            Math.Min(spatialLevel, MaximumSpatialLevel)];
    }

    public int VerticalSliceBudgetForSpatialLevel(int spatialLevel)
    {
        if (spatialLevel < 0) throw new ArgumentOutOfRangeException(nameof(spatialLevel));
        return _verticalSliceBudgetBySpatialLevel[
            Math.Min(spatialLevel, MaximumSpatialLevel)];
    }
}

public readonly record struct TerrainLodTileSelection(
    TerrainLodTileKey Tile,
    int HorizontalSampleLevel,
    int MaximumVerticalSlices);

public sealed class TerrainLodSpatialSelection
{
    internal TerrainLodSpatialSelection(
        TerrainLodTileSelection[] nodes,
        bool completeCoverage,
        int parentFallbacks,
        int missingCoverageGroups)
    {
        Nodes = Array.AsReadOnly(nodes);
        CompleteCoverage = completeCoverage;
        ParentFallbacks = parentFallbacks;
        MissingCoverageGroups = missingCoverageGroups;
    }

    public ReadOnlyCollection<TerrainLodTileSelection> Nodes { get; }
    public bool CompleteCoverage { get; }
    public int ParentFallbacks { get; }
    public int MissingCoverageGroups { get; }
}

/// <summary>
///     Pure coverage selector for a single spatial root. A child group becomes visible only when
///     all four quadrants have coverage. Otherwise the last ready parent remains selected.
/// </summary>
public static class TerrainLodSpatialSelector
{
    public static TerrainLodSpatialSelection Select(
        TerrainLodTileKey root,
        double cameraChunkX,
        double cameraChunkZ,
        TerrainLodSpatialPolicy policy,
        Func<TerrainLodTileKey, bool> isGpuReady)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(isGpuReady);
        List<TerrainLodTileSelection> selected = [];
        var parentFallbacks = 0;
        var missingCoverageGroups = 0;
        var complete = Cover(root);
        if (!complete) selected.Clear();
        return new TerrainLodSpatialSelection(
            [.. selected], complete, parentFallbacks, missingCoverageGroups);

        bool Cover(TerrainLodTileKey tile)
        {
            var desiredLevel = policy.DesiredSpatialLevel(
                tile.DistanceTo(cameraChunkX, cameraChunkZ));
            var shouldRefine = tile.Level > desiredLevel;
            if (!shouldRefine && isGpuReady(tile))
            {
                selected.Add(new TerrainLodTileSelection(
                    tile,
                    policy.HorizontalSampleLevelForSpatialLevel(tile.Level),
                    policy.VerticalSliceBudgetForSpatialLevel(tile.Level)));
                return true;
            }

            if (tile.Level > 0)
            {
                var childStart = selected.Count;
                var allChildrenCovered = true;
                for (var i = 0; i < 4; i++)
                    allChildrenCovered &= Cover(tile.Child(i));
                if (allChildrenCovered) return true;
                selected.RemoveRange(childStart, selected.Count - childStart);
            }

            if (isGpuReady(tile))
            {
                if (shouldRefine) parentFallbacks++;
                selected.Add(new TerrainLodTileSelection(
                    tile,
                    policy.HorizontalSampleLevelForSpatialLevel(tile.Level),
                    policy.VerticalSliceBudgetForSpatialLevel(tile.Level)));
                return true;
            }

            missingCoverageGroups++;
            return false;
        }
    }
}

public sealed class TerrainLodSpatialForestRootSelection
{
    internal TerrainLodSpatialForestRootSelection(
        TerrainLodTileKey managementRoot,
        TerrainLodTileSelection[] nodes,
        bool completeCoverage,
        int parentFallbacks,
        int missingCoverageGroups)
    {
        ManagementRoot = managementRoot;
        Nodes = Array.AsReadOnly(nodes);
        CompleteCoverage = completeCoverage;
        ParentFallbacks = parentFallbacks;
        MissingCoverageGroups = missingCoverageGroups;
    }

    public TerrainLodTileKey ManagementRoot { get; }
    public ReadOnlyCollection<TerrainLodTileSelection> Nodes { get; }
    public bool CompleteCoverage { get; }
    public int ParentFallbacks { get; }
    public int MissingCoverageGroups { get; }
}

public sealed class TerrainLodSpatialForestSelection
{
    internal TerrainLodSpatialForestSelection(
        TerrainLodSpatialForestRootSelection[] roots)
    {
        Roots = Array.AsReadOnly(roots);
    }

    public ReadOnlyCollection<TerrainLodSpatialForestRootSelection> Roots { get; }
    public int SelectedNodes => Roots.Sum(static root => root.Nodes.Count);
}

/// <summary>
///     Builds a deterministic forest over every GPU-ready spatial presentation near the camera.
///     Maximum-level roots are management domains only: an incomplete domain recursively exposes
///     its independently covered descendants instead of turning the missing domain into a hole.
///     Once a domain becomes complete, the ordinary all-children-or-parent selector can promote
///     or demote its whole partition atomically.
/// </summary>
public static class TerrainLodSpatialForestSelector
{
    public static TerrainLodSpatialForestSelection Select(
        IEnumerable<TerrainLodTileKey> readyKeys,
        int minimumVisibleLevel,
        double cameraChunkX,
        double cameraChunkZ,
        double maximumDistanceChunks,
        TerrainLodSpatialPolicy policy,
        Func<TerrainLodTileKey, bool> isGpuReady)
    {
        ArgumentNullException.ThrowIfNull(readyKeys);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(isGpuReady);
        if (minimumVisibleLevel is < 0 or > TerrainLodTileKey.MaximumLevel)
            throw new ArgumentOutOfRangeException(nameof(minimumVisibleLevel));
        if (minimumVisibleLevel > policy.MaximumSpatialLevel)
            throw new ArgumentOutOfRangeException(nameof(minimumVisibleLevel));
        if (!double.IsFinite(maximumDistanceChunks) || maximumDistanceChunks < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumDistanceChunks));

        var managementRoots = readyKeys
            .Where(key => key.Level >= minimumVisibleLevel &&
                          key.Level <= policy.MaximumSpatialLevel)
            .Where(key => key.DistanceTo(cameraChunkX, cameraChunkZ) <=
                          maximumDistanceChunks)
            .Select(key => AncestorAt(key, policy.MaximumSpatialLevel))
            .Distinct()
            .OrderBy(key => key.DistanceTo(cameraChunkX, cameraChunkZ))
            .ThenBy(static key => key.X)
            .ThenBy(static key => key.Z)
            .ToArray();

        List<TerrainLodSpatialForestRootSelection> roots = [];
        foreach (var managementRoot in managementRoots)
        {
            List<TerrainLodTileSelection> nodes = [];
            var parentFallbacks = 0;
            var missingCoverageGroups = 0;
            var complete = Cover(managementRoot, nodes,
                ref parentFallbacks, ref missingCoverageGroups);
            if (nodes.Count == 0) continue;
            roots.Add(new TerrainLodSpatialForestRootSelection(
                managementRoot, [.. nodes], complete,
                parentFallbacks, missingCoverageGroups));
        }

        return new TerrainLodSpatialForestSelection([.. roots]);

        bool Cover(
            TerrainLodTileKey root,
            List<TerrainLodTileSelection> destination,
            ref int parentFallbacks,
            ref int missingCoverageGroups)
        {
            var selection = TerrainLodSpatialSelector.Select(
                root, cameraChunkX, cameraChunkZ, policy, isGpuReady);
            parentFallbacks += selection.ParentFallbacks;
            missingCoverageGroups += selection.MissingCoverageGroups;
            if (selection.CompleteCoverage)
            {
                destination.AddRange(selection.Nodes);
                return true;
            }

            if (root.Level == minimumVisibleLevel) return false;
            var allChildrenComplete = true;
            for (var index = 0; index < 4; index++)
                allChildrenComplete &= Cover(
                    root.Child(index), destination,
                    ref parentFallbacks, ref missingCoverageGroups);
            return allChildrenComplete;
        }
    }

    private static TerrainLodTileKey AncestorAt(TerrainLodTileKey key, int level)
    {
        while (key.Level < level) key = key.Parent();
        return key;
    }
}
