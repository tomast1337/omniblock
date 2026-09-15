using OmniBlock.Client.Rendering;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using Silk.NET.Maths;
using System.Diagnostics;

namespace OmniBlock.Client.Rendering.Chunks;

/// <summary>
///     Render-thread-owned spatial index for published section presentations. The world already
///     has a regular grid, so a small fixed hierarchy is cheaper and more deterministic than a
///     general scene tree: 8x8 chunk-column regions, columns, then fixed vertical section slots.
/// </summary>
internal sealed class ResidentSectionSpatialIndex
{
    internal const int RegionWidthInColumns = 8;
    private static readonly int s_sectionCount = ChuckFormat.WorldHeight / SubChunkRenderer.Size;

    private readonly Dictionary<RegionKey, RegionEntry> _regions = [];
    private readonly SectionDistanceComparer _distanceComparer = new();

    public int Count { get; private set; }
    public int SolidLayerCount { get; private set; }
    public int TranslucentLayerCount { get; private set; }
    internal int RegionCount => _regions.Count;

    public void AddOrUpdate(SubChunkRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        var position = renderer.Position;
        if (!ChunkRenderer.IsValidWorldSectionY(position.Y) ||
            position.X % SubChunkRenderer.Size != 0 ||
            position.Z % SubChunkRenderer.Size != 0)
            throw new ArgumentOutOfRangeException(nameof(renderer),
                $"Render section {position} is not aligned to the world section grid.");

        var chunkX = position.X / SubChunkRenderer.Size;
        var chunkZ = position.Z / SubChunkRenderer.Size;
        var regionKey = new RegionKey(FloorDiv(chunkX, RegionWidthInColumns),
            FloorDiv(chunkZ, RegionWidthInColumns));
        if (!_regions.TryGetValue(regionKey, out var region))
        {
            region = new RegionEntry(regionKey);
            _regions.Add(regionKey, region);
        }

        var columnKey = new ColumnKey(chunkX, chunkZ);
        if (!region.Columns.TryGetValue(columnKey, out var column))
        {
            column = new ColumnEntry(columnKey);
            region.Columns.Add(columnKey, column);
        }

        var sectionY = position.Y / SubChunkRenderer.Size;
        var indexed = column.Sections[sectionY];
        if (indexed == null)
        {
            indexed = new IndexedSection(renderer);
            column.Sections[sectionY] = indexed;
            Count++;
            column.Count++;
            region.Count++;
        }
        else if (!ReferenceEquals(indexed.Renderer, renderer))
        {
            throw new InvalidOperationException(
                $"Spatial slot {position} already belongs to a different renderer.");
        }

        UpdateLayerSummary(indexed, column, region,
            renderer.HasSolidGeometry, renderer.HasTranslucentGeometry);
    }

    public bool Remove(SubChunkRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        var position = renderer.Position;
        var chunkX = position.X / SubChunkRenderer.Size;
        var chunkZ = position.Z / SubChunkRenderer.Size;
        var regionKey = new RegionKey(FloorDiv(chunkX, RegionWidthInColumns),
            FloorDiv(chunkZ, RegionWidthInColumns));
        if (!_regions.TryGetValue(regionKey, out var region)) return false;

        var columnKey = new ColumnKey(chunkX, chunkZ);
        if (!region.Columns.TryGetValue(columnKey, out var column)) return false;
        var sectionY = position.Y / SubChunkRenderer.Size;
        if ((uint)sectionY >= column.Sections.Length) return false;
        var indexed = column.Sections[sectionY];
        if (indexed == null || !ReferenceEquals(indexed.Renderer, renderer)) return false;

        if (indexed.HasSolid)
        {
            SolidLayerCount--;
            column.SolidLayerCount--;
            region.SolidLayerCount--;
        }
        if (indexed.HasTranslucent)
        {
            TranslucentLayerCount--;
            column.TranslucentLayerCount--;
            region.TranslucentLayerCount--;
        }

        column.Sections[sectionY] = null;
        Count--;
        column.Count--;
        region.Count--;
        if (column.Count == 0) region.Columns.Remove(columnKey);
        if (region.Count == 0) _regions.Remove(regionKey);
        return true;
    }

    /// <summary>
    ///     Writes exact-frustum section candidates into a caller-owned list. Coarse rejection is
    ///     conservative: portal traversal may still follow adjacency through the expanded margin.
    /// </summary>
    public SpatialQueryDiagnostics Query(
        ICuller culler,
        Vector3D<double> viewPosition,
        float renderDistance,
        List<SubChunkRenderer> destination)
    {
        var cullStarted = Stopwatch.GetTimestamp();
        destination.Clear();
        var regionTests = 0;
        var columnTests = 0;
        var sectionTests = 0;
        var outsideRenderDistance = 0;

        foreach (var region in _regions.Values)
        {
            regionTests++;
            if (!culler.IsBoundingBoxInFrustum(region.Bounds)) continue;

            foreach (var column in region.Columns.Values)
            {
                columnTests++;
                if (!culler.IsBoundingBoxInFrustum(column.Bounds)) continue;

                for (var y = 0; y < column.Sections.Length; y++)
                {
                    var indexed = column.Sections[y];
                    if (indexed == null) continue;
                    sectionTests++;
                    if (!culler.IsBoundingBoxInFrustum(indexed.Renderer.BoundingBox)) continue;
                    destination.Add(indexed.Renderer);
                    // Keep this as a diagnostic first. The portal culler still owns the final
                    // render-distance decision, so this does not alter conservative visibility.
                    // It tells us exactly how many frustum survivors the sort processes only to
                    // reject them at the next stage.
                    if (!indexed.Renderer.IsWithinRenderDistance(viewPosition, renderDistance))
                        outsideRenderDistance++;
                }
            }
        }

        var cullMs = Stopwatch.GetElapsedTime(cullStarted).TotalMilliseconds;
        _distanceComparer.Origin = viewPosition;
        _distanceComparer.Comparisons = 0;
        var sortStarted = Stopwatch.GetTimestamp();
        destination.Sort(_distanceComparer);
        var sortMs = Stopwatch.GetElapsedTime(sortStarted).TotalMilliseconds;
        return new SpatialQueryDiagnostics(
            regionTests,
            columnTests,
            sectionTests,
            destination.Count,
            outsideRenderDistance,
            _distanceComparer.Comparisons,
            cullMs,
            sortMs);
    }

    public bool Contains(SubChunkRenderer renderer)
    {
        var position = renderer.Position;
        var chunkX = position.X / SubChunkRenderer.Size;
        var chunkZ = position.Z / SubChunkRenderer.Size;
        var regionKey = new RegionKey(FloorDiv(chunkX, RegionWidthInColumns),
            FloorDiv(chunkZ, RegionWidthInColumns));
        if (!_regions.TryGetValue(regionKey, out var region) ||
            !region.Columns.TryGetValue(new ColumnKey(chunkX, chunkZ), out var column)) return false;
        var sectionY = position.Y / SubChunkRenderer.Size;
        return (uint)sectionY < column.Sections.Length &&
               ReferenceEquals(column.Sections[sectionY]?.Renderer, renderer);
    }

    internal SpatialLayerSummary GetSummary(Vector3D<int> sectionPosition)
    {
        var chunkX = sectionPosition.X / SubChunkRenderer.Size;
        var chunkZ = sectionPosition.Z / SubChunkRenderer.Size;
        var key = new RegionKey(FloorDiv(chunkX, RegionWidthInColumns),
            FloorDiv(chunkZ, RegionWidthInColumns));
        if (!_regions.TryGetValue(key, out var region)) return default;
        region.Columns.TryGetValue(new ColumnKey(chunkX, chunkZ), out var column);
        return new SpatialLayerSummary(
            region.Count, region.SolidLayerCount, region.TranslucentLayerCount,
            column?.Count ?? 0, column?.SolidLayerCount ?? 0, column?.TranslucentLayerCount ?? 0);
    }

    public void Clear()
    {
        _regions.Clear();
        Count = 0;
        SolidLayerCount = 0;
        TranslucentLayerCount = 0;
    }

    internal void Validate(IEnumerable<SectionRenderState> authoritative)
    {
        var count = 0;
        var solid = 0;
        var translucent = 0;
        foreach (var state in authoritative)
        {
            var renderer = state.Renderer ?? throw new InvalidOperationException(
                $"Authoritative resident section {state.Position} has no renderer.");
            if (!Contains(renderer))
                throw new InvalidOperationException($"Spatial index is missing resident section {state.Position}.");
            count++;
            if (renderer.HasSolidGeometry) solid++;
            if (renderer.HasTranslucentGeometry) translucent++;
        }

        if (count != Count || solid != SolidLayerCount || translucent != TranslucentLayerCount)
            throw new InvalidOperationException(
                $"Spatial index mismatch: authoritative={count}/{solid}/{translucent}, " +
                $"indexed={Count}/{SolidLayerCount}/{TranslucentLayerCount}.");
    }

    private void UpdateLayerSummary(
        IndexedSection indexed,
        ColumnEntry column,
        RegionEntry region,
        bool hasSolid,
        bool hasTranslucent)
    {
        var solidDelta = Convert.ToInt32(hasSolid) - Convert.ToInt32(indexed.HasSolid);
        var translucentDelta = Convert.ToInt32(hasTranslucent) - Convert.ToInt32(indexed.HasTranslucent);
        indexed.HasSolid = hasSolid;
        indexed.HasTranslucent = hasTranslucent;
        SolidLayerCount += solidDelta;
        TranslucentLayerCount += translucentDelta;
        column.SolidLayerCount += solidDelta;
        column.TranslucentLayerCount += translucentDelta;
        region.SolidLayerCount += solidDelta;
        region.TranslucentLayerCount += translucentDelta;
    }

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
    }

    private readonly record struct RegionKey(int X, int Z);
    private readonly record struct ColumnKey(int X, int Z);

    private sealed class RegionEntry
    {
        public RegionEntry(RegionKey key)
        {
            var minX = key.X * RegionWidthInColumns * SubChunkRenderer.Size;
            var minZ = key.Z * RegionWidthInColumns * SubChunkRenderer.Size;
            Bounds = new Box(
                minX - SubChunkRenderer.BoundsPadding,
                -SubChunkRenderer.BoundsPadding,
                minZ - SubChunkRenderer.BoundsPadding,
                minX + RegionWidthInColumns * SubChunkRenderer.Size + SubChunkRenderer.BoundsPadding,
                ChuckFormat.WorldHeight + SubChunkRenderer.BoundsPadding,
                minZ + RegionWidthInColumns * SubChunkRenderer.Size + SubChunkRenderer.BoundsPadding);
        }

        public Box Bounds { get; }
        public Dictionary<ColumnKey, ColumnEntry> Columns { get; } = [];
        public int Count;
        public int SolidLayerCount;
        public int TranslucentLayerCount;
    }

    private sealed class ColumnEntry
    {
        public ColumnEntry(ColumnKey key)
        {
            var minX = key.X * SubChunkRenderer.Size;
            var minZ = key.Z * SubChunkRenderer.Size;
            Bounds = new Box(
                minX - SubChunkRenderer.BoundsPadding,
                -SubChunkRenderer.BoundsPadding,
                minZ - SubChunkRenderer.BoundsPadding,
                minX + SubChunkRenderer.Size + SubChunkRenderer.BoundsPadding,
                ChuckFormat.WorldHeight + SubChunkRenderer.BoundsPadding,
                minZ + SubChunkRenderer.Size + SubChunkRenderer.BoundsPadding);
        }

        public Box Bounds { get; }
        public IndexedSection?[] Sections { get; } = new IndexedSection?[s_sectionCount];
        public int Count;
        public int SolidLayerCount;
        public int TranslucentLayerCount;
    }

    private sealed class IndexedSection(SubChunkRenderer renderer)
    {
        public SubChunkRenderer Renderer { get; } = renderer;
        public bool HasSolid;
        public bool HasTranslucent;
    }

    private sealed class SectionDistanceComparer : IComparer<SubChunkRenderer>
    {
        public Vector3D<double> Origin;
        public int Comparisons;

        public int Compare(SubChunkRenderer? left, SubChunkRenderer? right)
        {
            Comparisons++;
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return 1;
            if (right == null) return -1;

            var distance = DistanceSquared(left.PositionPlus, Origin)
                .CompareTo(DistanceSquared(right.PositionPlus, Origin));
            if (distance != 0) return distance;
            var x = left.Position.X.CompareTo(right.Position.X);
            if (x != 0) return x;
            var y = left.Position.Y.CompareTo(right.Position.Y);
            return y != 0 ? y : left.Position.Z.CompareTo(right.Position.Z);
        }

        private static double DistanceSquared(Vector3D<int> position, Vector3D<double> origin)
        {
            var dx = position.X - origin.X;
            var dy = position.Y - origin.Y;
            var dz = position.Z - origin.Z;
            return dx * dx + dy * dy + dz * dz;
        }
    }
}

internal readonly record struct SpatialQueryDiagnostics(
    int RegionTests,
    int ColumnTests,
    int SectionTests,
    int Candidates,
    int OutsideRenderDistance,
    int SortComparisons,
    double CullMs,
    double SortMs)
{
    public int FrustumTests => RegionTests + ColumnTests + SectionTests;
}

internal readonly record struct SpatialLayerSummary(
    int RegionSections,
    int RegionSolidLayers,
    int RegionTranslucentLayers,
    int ColumnSections,
    int ColumnSolidLayers,
    int ColumnTranslucentLayers);
