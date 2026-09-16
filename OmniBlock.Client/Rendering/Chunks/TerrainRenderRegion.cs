using System.Runtime.InteropServices;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.WebGPU;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;

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

    public bool Owns(in TerrainGpuAllocation allocation) =>
        allocation.Id > 0 && _live.TryGetValue(allocation.Id, out var owned) &&
        owned == allocation && !_retiredIds.Contains(allocation.Id);

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

    private static int AlignUp(int value, int alignment) => checked((int)(
        ((long)value + alignment - 1) / alignment * alignment));

    private static void ValidateAlignment(int alignment)
    {
        if (alignment <= 0)
            throw new ArgumentOutOfRangeException(nameof(alignment),
                "Alignment must be positive.");
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

/// <summary>
///     One paired geometry/light range. Both streams share the same logical vertex base, allowing
///     a whole regional buffer pair to remain bound while indexed draws select pages by baseVertex.
/// </summary>
internal readonly unsafe struct TerrainGpuVertexSlice
{
    public TerrainGpuVertexSlice(
        WgpuBuffer* geometryBuffer,
        WgpuBuffer* lightingBuffer,
        int firstVertex,
        int vertexCount)
    {
        GeometryBuffer = geometryBuffer;
        LightingBuffer = lightingBuffer;
        FirstVertex = firstVertex;
        VertexCount = vertexCount;
    }

    public WgpuBuffer* GeometryBuffer { get; }
    public WgpuBuffer* LightingBuffer { get; }
    public int FirstVertex { get; }
    public int VertexCount { get; }
}

internal readonly record struct TerrainGpuArenaSnapshot(
    int Regions,
    int Segments,
    long CapacityBytes,
    long AllocatedBytes,
    long FreeBytes,
    long LargestFreeRangeBytes,
    long FragmentedFreeBytes,
    int ActiveAllocations,
    int PendingRetirements,
    long SegmentGrowths,
    long FailedAllocations)
{
    public double ExternalFragmentation => FreeBytes == 0
        ? 0
        : FragmentedFreeBytes / (double)FreeBytes;
}

/// <summary>
///     Render-thread-owned regional terrain storage. Every fixed-size segment owns a geometry and
///     light buffer addressed by one shared vertex allocator. Segments append without moving a
///     published range.
/// </summary>
internal sealed unsafe class TerrainGpuArenaSet : IDisposable
{
    internal const int DefaultSegmentVertices = 64 * 1024;
    private const int QuadAlignmentBytes = 4 * (int)WgpuMesh.ChunkVertexStride;

    private readonly WebGpuDevice _device;
    private readonly int _ownerThreadId;
    private readonly Dictionary<TerrainRenderRegionKey, Region> _regions = [];
    private readonly List<TerrainRenderRegionKey> _emptyRegions = [];
    private bool _disposed;
    private long _segmentGrowths;

    public TerrainGpuArenaSet(WebGpuDevice device)
    {
        _device = device;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public TerrainGpuVertexLease Upload(
        TerrainRenderRegionKey regionKey,
        ReadOnlySpan<ChunkVertex> vertices,
        ReadOnlySpan<ChunkLightVertex> lighting)
    {
        AssertOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (vertices.IsEmpty || vertices.Length % 4 != 0)
            throw new ArgumentException(
                "Terrain GPU uploads require a non-empty multiple of four vertices.", nameof(vertices));
        if (lighting.Length != vertices.Length)
            throw new ArgumentException(
                "Terrain geometry and lighting must contain the same number of vertices.", nameof(lighting));

        if (!_regions.TryGetValue(regionKey, out var region))
        {
            region = new Region();
            _regions.Add(regionKey, region);
        }

        foreach (var segment in region.Segments)
        {
            var geometryBytes = checked(vertices.Length * (int)WgpuMesh.ChunkVertexStride);
            if (segment.Allocator.Snapshot().LargestFreeRangeBytes < geometryBytes) continue;
            if (segment.TryUpload(vertices, lighting, out var lease)) return lease;
        }

        var capacityVertices = Math.Max(DefaultSegmentVertices, RoundUpPowerOfTwo(vertices.Length));
        var newSegment = new Segment(_device, capacityVertices);
        region.Segments.Add(newSegment);
        _segmentGrowths++;
        if (newSegment.TryUpload(vertices, lighting, out var createdLease)) return createdLease;

        throw new InvalidOperationException(
            $"A new {capacityVertices}-vertex terrain GPU segment could not hold " +
            $"a {vertices.Length}-vertex upload.");
    }

    /// <summary>Returns retired ranges to their allocators after the preceding frame was submitted.</summary>
    public int EndFrame()
    {
        AssertOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        var released = 0;
        foreach (var (key, region) in _regions)
        {
            foreach (var segment in region.Segments) released += segment.Allocator.ReleaseRetired();
            if (region.IsEmpty) _emptyRegions.Add(key);
        }

        // A moving player must not leave one arena cache behind for every region ever visited.
        // Buffer destruction is itself deferred by the device, so removing an empty owner here is
        // safe even though the just-submitted frame may still be executing its final draws.
        foreach (var key in _emptyRegions)
        {
            if (!_regions.Remove(key, out var region)) continue;
            region.Dispose();
        }
        _emptyRegions.Clear();
        return released;
    }

    public TerrainGpuArenaSnapshot Snapshot()
    {
        AssertOwnerThread();
        var segments = 0;
        long capacity = 0;
        long allocated = 0;
        long free = 0;
        long largest = 0;
        long fragmentedFree = 0;
        var active = 0;
        var retired = 0;
        long failed = 0;

        foreach (var region in _regions.Values)
        {
            segments += region.Segments.Count;
            foreach (var segment in region.Segments)
            {
                var snapshot = segment.Allocator.Snapshot();
                capacity += PhysicalBytes(snapshot.CapacityBytes);
                allocated += PhysicalBytes(snapshot.AllocatedBytes);
                free += PhysicalBytes(snapshot.FreeBytes);
                largest = Math.Max(largest, PhysicalBytes(snapshot.LargestFreeRangeBytes));
                fragmentedFree += PhysicalBytes(
                    snapshot.FreeBytes - snapshot.LargestFreeRangeBytes);
                active += snapshot.ActiveAllocations;
                retired += snapshot.PendingRetirements;
                failed += snapshot.FailedAllocations;
            }
        }

        return new TerrainGpuArenaSnapshot(
            _regions.Count, segments, capacity, allocated, free,
            largest, fragmentedFree, active, retired, _segmentGrowths, failed);
    }

    private static long PhysicalBytes(long geometryBytes) => checked(
        geometryBytes + geometryBytes / WgpuMesh.ChunkVertexStride * WgpuMesh.ChunkLightVertexStride);

    public void Dispose()
    {
        if (_disposed) return;
        AssertOwnerThread();
        _disposed = true;
        foreach (var region in _regions.Values) region.Dispose();
        _regions.Clear();
        _emptyRegions.Clear();
    }

    private void AssertOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Terrain GPU arenas may only be used by their render thread owner.");
    }

    private static int RoundUpPowerOfTwo(int value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (value > 1 << 30) return value;
        return (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)value);
    }

    private sealed class Region
    {
        public List<Segment> Segments { get; } = [];

        public bool IsEmpty => Segments.All(
            static segment => segment.Allocator.Snapshot().AllocatedBytes == 0);

        public void Dispose()
        {
            foreach (var segment in Segments) segment.Dispose();
            Segments.Clear();
        }
    }

    internal sealed class Segment : IDisposable
    {
        private readonly WebGpuDevice _device;
        private bool _disposed;

        public Segment(WebGpuDevice device, int capacityVertices)
        {
            _device = device;
            var geometryBytes = checked(capacityVertices * (int)WgpuMesh.ChunkVertexStride);
            var lightingBytes = checked(capacityVertices * (int)WgpuMesh.ChunkLightVertexStride);
            Allocator = new TerrainGpuRangeAllocator(geometryBytes);
            BufferDescriptor geometryDescriptor = new()
            {
                Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
                Size = (ulong)geometryBytes
            };
            GeometryBuffer = device.Api.DeviceCreateBuffer(device.Device, in geometryDescriptor);
            if (GeometryBuffer == null)
                throw new InvalidOperationException("WebGPU did not create a terrain geometry arena buffer.");

            try
            {
                BufferDescriptor lightingDescriptor = new()
                {
                    Usage = BufferUsage.Vertex | BufferUsage.CopyDst,
                    Size = (ulong)lightingBytes
                };
                LightingBuffer = device.Api.DeviceCreateBuffer(device.Device, in lightingDescriptor);
                if (LightingBuffer == null)
                    throw new InvalidOperationException("WebGPU did not create a terrain lighting arena buffer.");
            }
            catch
            {
                WgpuRelease.DeferredBuffers(device, (nint)GeometryBuffer);
                throw;
            }
        }

        public WgpuBuffer* GeometryBuffer { get; }
        public WgpuBuffer* LightingBuffer { get; }
        public TerrainGpuRangeAllocator Allocator { get; }

        public bool TryUpload(
            ReadOnlySpan<ChunkVertex> vertices,
            ReadOnlySpan<ChunkLightVertex> lighting,
            out TerrainGpuVertexLease lease)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var geometry = MemoryMarshal.AsBytes(vertices);
            var lights = MemoryMarshal.AsBytes(lighting);
            if (!Allocator.TryAllocate(geometry.Length, QuadAlignmentBytes, out var allocation))
            {
                lease = null!;
                return false;
            }

            try
            {
                fixed (byte* geometryData = geometry)
                    _device.Api.QueueWriteBuffer(
                        _device.Queue, GeometryBuffer, (ulong)allocation.OffsetBytes,
                        geometryData, (nuint)geometry.Length);
                var firstVertex = allocation.OffsetBytes / (int)WgpuMesh.ChunkVertexStride;
                fixed (byte* lightData = lights)
                    _device.Api.QueueWriteBuffer(
                        _device.Queue, LightingBuffer,
                        checked((ulong)firstVertex * WgpuMesh.ChunkLightVertexStride),
                        lightData, (nuint)lights.Length);
                lease = new TerrainGpuVertexLease(this, allocation);
                return true;
            }
            catch
            {
                Allocator.Release(allocation);
                throw;
            }
        }

        public TerrainGpuVertexSlice Slice(in TerrainGpuAllocation allocation) => new(
            GeometryBuffer,
            LightingBuffer,
            allocation.OffsetBytes / (int)WgpuMesh.ChunkVertexStride,
            allocation.LengthBytes / (int)WgpuMesh.ChunkVertexStride);

        public void RewriteLighting(
            in TerrainGpuAllocation allocation,
            ReadOnlySpan<ChunkLightVertex> values)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!Allocator.Owns(allocation))
                throw new InvalidOperationException("Cannot update a stale terrain lighting range.");
            var expectedVertices = allocation.LengthBytes / (int)WgpuMesh.ChunkVertexStride;
            if (values.Length != expectedVertices)
                throw new ArgumentException("Replacement lighting must match its paired geometry.", nameof(values));

            var bytes = MemoryMarshal.AsBytes(values);
            var firstVertex = allocation.OffsetBytes / (int)WgpuMesh.ChunkVertexStride;
            fixed (byte* data = bytes)
                _device.Api.QueueWriteBuffer(
                    _device.Queue, LightingBuffer,
                    checked((ulong)firstVertex * WgpuMesh.ChunkLightVertexStride),
                    data, (nuint)bytes.Length);
        }

        public void Retire(in TerrainGpuAllocation allocation)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Allocator.Retire(allocation);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            WgpuRelease.DeferredBuffers(
                _device, (nint)GeometryBuffer, (nint)LightingBuffer);
        }
    }
}

/// <summary>Exclusive ownership of one paired vertex range.</summary>
internal sealed class TerrainGpuVertexLease : IDisposable
{
    private TerrainGpuArenaSet.Segment? _segment;
    private readonly TerrainGpuAllocation _allocation;

    internal TerrainGpuVertexLease(
        TerrainGpuArenaSet.Segment segment,
        TerrainGpuAllocation allocation)
    {
        _segment = segment;
        _allocation = allocation;
    }

    public TerrainGpuVertexSlice Slice => (_segment ??
        throw new ObjectDisposedException(nameof(TerrainGpuVertexLease))).Slice(_allocation);

    public void RewriteLighting(ReadOnlySpan<ChunkLightVertex> values) => (_segment ??
        throw new ObjectDisposedException(nameof(TerrainGpuVertexLease)))
        .RewriteLighting(_allocation, values);

    public void Dispose()
    {
        Interlocked.Exchange(ref _segment, null)?.Retire(_allocation);
    }
}
