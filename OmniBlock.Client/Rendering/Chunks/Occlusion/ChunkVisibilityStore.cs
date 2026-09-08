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

        // The camera's angle to the section center cannot prove whole-face connectivity hidden.
        // Off-axis rays may still cross opposite faces, so use the compiled portal data directly.
        var mask = CreateMask((int)incoming);
        return FoldOutgoing(_data & mask);
    }

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
