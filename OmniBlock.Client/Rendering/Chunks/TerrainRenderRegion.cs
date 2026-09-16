using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Chunks;

/// <summary>
///     Stable address of one near-terrain GPU arena. Horizontal coverage matches the visibility
///     index's 8x8 chunk regions; four vertical sections keep one arena from spanning the world.
/// </summary>
internal readonly record struct TerrainRenderRegionKey(int X, int Y, int Z)
{
    public const int WidthInColumns = ResidentSectionSpatialIndex.RegionWidthInColumns;
    public const int HeightInSections = 4;
    public const int WidthInBlocks = WidthInColumns * SubChunkRenderer.Size;
    public const int HeightInBlocks = HeightInSections * SubChunkRenderer.Size;

    public Vector3D<int> Origin => new(
        X * WidthInBlocks,
        Y * HeightInBlocks,
        Z * WidthInBlocks);

    public static TerrainRenderRegionKey FromSectionPosition(Vector3D<int> sectionPosition)
    {
        if (sectionPosition.X % SubChunkRenderer.Size != 0 ||
            sectionPosition.Y % SubChunkRenderer.Size != 0 ||
            sectionPosition.Z % SubChunkRenderer.Size != 0)
            throw new ArgumentOutOfRangeException(nameof(sectionPosition),
                $"Terrain section {sectionPosition} is not aligned to {SubChunkRenderer.Size} blocks.");

        var sectionX = sectionPosition.X / SubChunkRenderer.Size;
        var sectionY = sectionPosition.Y / SubChunkRenderer.Size;
        var sectionZ = sectionPosition.Z / SubChunkRenderer.Size;
        return new TerrainRenderRegionKey(
            FloorDiv(sectionX, WidthInColumns),
            FloorDiv(sectionY, HeightInSections),
            FloorDiv(sectionZ, WidthInColumns));
    }

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
    }
}

internal readonly record struct TerrainGpuAllocation(long Id, int OffsetBytes, int LengthBytes)
{
    public int EndBytes => checked(OffsetBytes + LengthBytes);
}

internal readonly record struct TerrainGpuCompactionMove(
    TerrainGpuAllocation Source,
    int DestinationOffsetBytes);

internal sealed record TerrainGpuCompactionPlan(
    int RequiredBytes,
    IReadOnlyList<TerrainGpuCompactionMove> Moves);

internal readonly record struct TerrainGpuRangeAllocatorSnapshot(
    int CapacityBytes,
    int AllocatedBytes,
    int FreeBytes,
    int LargestFreeRangeBytes,
    int ActiveAllocations,
    int PendingRetirements,
    int FreeRanges,
    long SuccessfulAllocations,
    long FailedAllocations,
    long Releases)
{
    public double ExternalFragmentation => FreeBytes == 0
        ? 0
        : 1.0 - LargestFreeRangeBytes / (double)FreeBytes;
}

/// <summary>
///     Deterministic CPU-side suballocator for a fixed WebGPU arena buffer. It deliberately does
///     not grow or mutate during compaction: the owner builds a replacement arena from
///     <see cref="CreateCompactionPlan"/>, publishes it atomically, then retires this allocator.
/// </summary>
internal sealed class TerrainGpuRangeAllocator
{
    private readonly List<FreeRange> _free;
    private readonly Dictionary<long, TerrainGpuAllocation> _live = [];
    private readonly Queue<TerrainGpuAllocation> _retired = [];
    private readonly HashSet<long> _retiredIds = [];
    private long _nextId = 1;
    private long _successfulAllocations;
    private long _failedAllocations;
    private long _releases;
    private int _allocatedBytes;

    public TerrainGpuRangeAllocator(int capacityBytes)
    {
        if (capacityBytes <= 0) throw new ArgumentOutOfRangeException(nameof(capacityBytes));
        CapacityBytes = capacityBytes;
        _free = [new FreeRange(0, capacityBytes)];
    }

    public int CapacityBytes { get; }

    public bool TryAllocate(int lengthBytes, int alignmentBytes, out TerrainGpuAllocation allocation)
    {
        if (lengthBytes <= 0) throw new ArgumentOutOfRangeException(nameof(lengthBytes));
        ValidateAlignment(alignmentBytes);

        var selected = -1;
        var selectedOffset = 0;
        var selectedWaste = int.MaxValue;
        for (var i = 0; i < _free.Count; i++)
        {
            var range = _free[i];
            var offset = AlignUp(range.OffsetBytes, alignmentBytes);
            var end = (long)offset + lengthBytes;
            if (end > range.EndBytes) continue;
            var waste = checked(range.LengthBytes - lengthBytes - (offset - range.OffsetBytes));
            if (waste > selectedWaste ||
                waste == selectedWaste && selected >= 0 && offset >= selectedOffset) continue;
            selected = i;
            selectedOffset = offset;
            selectedWaste = waste;
        }

        if (selected < 0)
        {
            _failedAllocations++;
            allocation = default;
            return false;
        }

        var source = _free[selected];
        _free.RemoveAt(selected);
        if (selectedOffset > source.OffsetBytes)
            InsertFree(new FreeRange(source.OffsetBytes, selectedOffset - source.OffsetBytes));
        var selectedEnd = checked(selectedOffset + lengthBytes);
        if (selectedEnd < source.EndBytes)
            InsertFree(new FreeRange(selectedEnd, source.EndBytes - selectedEnd));

        allocation = new TerrainGpuAllocation(_nextId++, selectedOffset, lengthBytes);
        _live.Add(allocation.Id, allocation);
        _allocatedBytes = checked(_allocatedBytes + lengthBytes);
        _successfulAllocations++;
        return true;
    }

    public void Release(in TerrainGpuAllocation allocation)
    {
        if (_retiredIds.Contains(allocation.Id))
            throw new InvalidOperationException(
                $"Terrain GPU allocation {allocation.Id} is already pending retirement.");
        ReleaseCore(allocation);
    }

    /// <summary>Stops logical ownership now, but keeps the bytes unavailable until the frame boundary.</summary>
    public void Retire(in TerrainGpuAllocation allocation)
    {
        ValidateOwned(allocation);
        if (!_retiredIds.Add(allocation.Id))
            throw new InvalidOperationException(
                $"Terrain GPU allocation {allocation.Id} is already pending retirement.");
        _retired.Enqueue(allocation);
    }

    /// <summary>Releases ranges only after commands using the previous presentation were submitted.</summary>
    public int ReleaseRetired()
    {
        var released = 0;
        while (_retired.TryDequeue(out var allocation))
        {
            if (!_retiredIds.Remove(allocation.Id))
                throw new InvalidOperationException("Terrain GPU retirement queue lost its allocation identity.");
            ReleaseCore(allocation);
            released++;
        }
        return released;
    }

    private void ReleaseCore(in TerrainGpuAllocation allocation)
    {
        if (allocation.Id <= 0 || !_live.Remove(allocation.Id, out var owned) || owned != allocation)
            throw new InvalidOperationException(
                $"Terrain GPU allocation {allocation.Id} is stale, foreign, or already released.");
        _allocatedBytes -= allocation.LengthBytes;
        _releases++;
        InsertFree(new FreeRange(allocation.OffsetBytes, allocation.LengthBytes));
        CoalesceFreeRanges();
    }

    public TerrainGpuCompactionPlan CreateCompactionPlan(int alignmentBytes)
    {
        ValidateAlignment(alignmentBytes);
        var cursor = 0;
        var moves = new List<TerrainGpuCompactionMove>(_live.Count);
        foreach (var allocation in _live.Values
                     .Where(allocation => !_retiredIds.Contains(allocation.Id))
                     .OrderBy(static value => value.OffsetBytes)
                     .ThenBy(static value => value.Id))
        {
            cursor = AlignUp(cursor, alignmentBytes);
            moves.Add(new TerrainGpuCompactionMove(allocation, cursor));
            cursor = checked(cursor + allocation.LengthBytes);
        }
        return new TerrainGpuCompactionPlan(cursor, moves);
    }

    public TerrainGpuRangeAllocatorSnapshot Snapshot()
    {
        var freeBytes = CapacityBytes - _allocatedBytes;
        return new TerrainGpuRangeAllocatorSnapshot(
            CapacityBytes,
            _allocatedBytes,
            freeBytes,
            _free.Count == 0 ? 0 : _free.Max(static range => range.LengthBytes),
            _live.Count - _retiredIds.Count,
            _retiredIds.Count,
            _free.Count,
            _successfulAllocations,
            _failedAllocations,
            _releases);
    }

    private void ValidateOwned(in TerrainGpuAllocation allocation)
    {
        if (allocation.Id <= 0 || !_live.TryGetValue(allocation.Id, out var owned) ||
            owned != allocation)
            throw new InvalidOperationException(
                $"Terrain GPU allocation {allocation.Id} is stale, foreign, or already released.");
    }

    private void InsertFree(FreeRange range)
    {
        var index = _free.BinarySearch(range, FreeRangeOffsetComparer.Instance);
        if (index < 0) index = ~index;
        _free.Insert(index, range);
    }

    private void CoalesceFreeRanges()
    {
        for (var i = 0; i + 1 < _free.Count;)
        {
            var left = _free[i];
            var right = _free[i + 1];
            if (left.EndBytes < right.OffsetBytes)
            {
                i++;
                continue;
            }
            if (left.EndBytes > right.OffsetBytes)
                throw new InvalidOperationException("Terrain GPU allocator free ranges overlap.");
            _free[i] = new FreeRange(left.OffsetBytes,
                checked(left.LengthBytes + right.LengthBytes));
            _free.RemoveAt(i + 1);
        }
    }

    private static int AlignUp(int value, int alignment) =>
        checked((value + alignment - 1) & -alignment);

    private static void ValidateAlignment(int alignment)
    {
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(alignment),
                "Alignment must be a positive power of two.");
    }

    private readonly record struct FreeRange(int OffsetBytes, int LengthBytes)
    {
        public int EndBytes => checked(OffsetBytes + LengthBytes);
    }

    private sealed class FreeRangeOffsetComparer : IComparer<FreeRange>
    {
        public static FreeRangeOffsetComparer Instance { get; } = new();
        public int Compare(FreeRange left, FreeRange right) =>
            left.OffsetBytes.CompareTo(right.OffsetBytes);
    }
}
