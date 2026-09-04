using System.Runtime.CompilerServices;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Chunks.Occlusion;

public struct ChunkVisibilityStore
{
    private long _data;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetVisible(ChunkDirection from, ChunkDirection to) => _data |= 1L << GetBit(from, to);

    public readonly ChunkDirectionMask GetVisibleFrom(ChunkDirectionMask incoming, Vector3D<double> viewPos, SubChunkRenderer renderer)
    {
        if (incoming == ChunkDirectionMask.None)
            return FoldOutgoing(_data);

        var visibilityData = _data;

        visibilityData &= GetAngleMask(viewPos, renderer);

        var mask = CreateMask((int)incoming);
        return FoldOutgoing(visibilityData & mask);
    }

    private static long GetAngleMask(Vector3D<double> viewPos, SubChunkRenderer renderer)
    {
        var center = renderer.PositionPlus;
        var dx = Math.Abs(viewPos.X - center.X);
        var dy = Math.Abs(viewPos.Y - center.Y);
        var dz = Math.Abs(viewPos.Z - center.Z);

        long mask = 0;
        if (dx > dy || dz > dy) mask |= GetUpDownOccluded();
        if (dx > dz || dy > dz) mask |= GetNorthSouthOccluded();
        if (dy > dx || dz > dx) mask |= GetWestEastOccluded();

        return ~mask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long GetUpDownOccluded() => (1L << GetBit(ChunkDirection.Down, ChunkDirection.Up)) | (1L << GetBit(ChunkDirection.Up, ChunkDirection.Down));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long GetNorthSouthOccluded() => (1L << GetBit(ChunkDirection.North, ChunkDirection.South)) | (1L << GetBit(ChunkDirection.South, ChunkDirection.North));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long GetWestEastOccluded() => (1L << GetBit(ChunkDirection.West, ChunkDirection.East)) | (1L << GetBit(ChunkDirection.East, ChunkDirection.West));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetBit(ChunkDirection from, ChunkDirection to) => ((int)from << 3) | (int)to;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long CreateMask(int incoming)
    {
        const long multiplier = 0x810204081L;
        var expanded = multiplier * (uint)incoming;
        return (expanded & 0x010101010101L) * 0xFF;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ChunkDirectionMask FoldOutgoing(long data)
    {
        var folded = data;
        folded |= folded >> 32;
        folded |= folded >> 16;
        folded |= folded >> 8;
        return (ChunkDirectionMask)(folded & (int)ChunkDirectionMask.All);
    }
}
