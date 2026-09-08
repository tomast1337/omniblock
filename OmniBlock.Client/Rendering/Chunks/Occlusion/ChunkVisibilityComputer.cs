using System.Runtime.CompilerServices;
using OmniBlock.Blocks;
using OmniBlock.Worlds.Core;

namespace OmniBlock.Client.Rendering.Chunks.Occlusion;

public static class ChunkVisibilityComputer
{
    private const int Size = SubChunkRenderer.Size;
    private const int TotalBlocks = Size * Size * Size;

    /// <summary>Compiles which boundary faces are connected through non-opaque cells.</summary>
    /// <remarks>
    ///     Connectivity belongs to transparent components, not to individual starting faces. The
    ///     previous implementation flood-filled the same open section once from each of its six
    ///     faces, repeating block lookups and traversal up to six times. This version classifies
    ///     opacity once and visits every reachable cell once, then connects every face touched by
    ///     that component. Closed interior cavities are deliberately ignored because no adjacent
    ///     section can enter them.
    /// </remarks>
    public static ChunkVisibilityStore Compute(WorldRegionSnapshot cache, int minX, int minY, int minZ)
    {
        ChunkVisibilityStore store = new();
        Span<uint> opaque = stackalloc uint[TotalBlocks / 32];
        Span<uint> visited = stackalloc uint[TotalBlocks / 32];
        Span<ushort> queue = stackalloc ushort[TotalBlocks];
        var opaqueCount = ClassifyOpacity(cache, minX, minY, minZ, opaque);

        if (opaqueCount == 0)
        {
            ConnectFaces(ref store, ChunkDirectionMask.All);
            return store;
        }

        for (var idx = 0; idx < TotalBlocks; idx++)
        {
            var x = idx & 0xF;
            var y = (idx >> 4) & 0xF;
            var z = (idx >> 8) & 0xF;
            if (!IsBoundary(x, y, z) || IsSet(opaque, idx) || IsSet(visited, idx)) continue;

            ConnectFaces(ref store, FloodComponent(idx, opaque, visited, queue));
        }

        return store;
    }

    private static int ClassifyOpacity(
        WorldRegionSnapshot cache,
        int minX, int minY, int minZ,
        Span<uint> opaque)
    {
        var count = 0;
        for (var z = 0; z < Size; z++)
        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            var id = cache.GetBlockId(minX + x, minY + y, minZ + z);
            if (id <= 0 || !cache.ContentBlocks.IsOpaque(id)) continue;

            Set(opaque, GetIndex(x, y, z));
            count++;
        }

        return count;
    }

    private static ChunkDirectionMask FloodComponent(
        int seed,
        ReadOnlySpan<uint> opaque,
        Span<uint> visited,
        Span<ushort> queue)
    {
        var head = 0;
        var tail = 0;
        var faces = ChunkDirectionMask.None;
        Set(visited, seed);
        queue[tail++] = (ushort)seed;

        while (head < tail)
        {
            var idx = queue[head++];
            var x = idx & 0xF;
            var y = (idx >> 4) & 0xF;
            var z = (idx >> 8) & 0xF;

            if (x == 0) faces |= ChunkDirectionMask.West;
            if (x == Size - 1) faces |= ChunkDirectionMask.East;
            if (y == 0) faces |= ChunkDirectionMask.Down;
            if (y == Size - 1) faces |= ChunkDirectionMask.Up;
            if (z == 0) faces |= ChunkDirectionMask.North;
            if (z == Size - 1) faces |= ChunkDirectionMask.South;

            if (x > 0) TryVisit(idx - 1, opaque, visited, queue, ref tail);
            if (x < Size - 1) TryVisit(idx + 1, opaque, visited, queue, ref tail);
            if (y > 0) TryVisit(idx - 16, opaque, visited, queue, ref tail);
            if (y < Size - 1) TryVisit(idx + 16, opaque, visited, queue, ref tail);
            if (z > 0) TryVisit(idx - 256, opaque, visited, queue, ref tail);
            if (z < Size - 1) TryVisit(idx + 256, opaque, visited, queue, ref tail);
        }

        return faces;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TryVisit(
        int idx,
        ReadOnlySpan<uint> opaque,
        Span<uint> visited,
        Span<ushort> queue,
        ref int tail)
    {
        if (IsSet(opaque, idx) || IsSet(visited, idx)) return;
        Set(visited, idx);
        queue[tail++] = (ushort)idx;
    }

    private static void ConnectFaces(ref ChunkVisibilityStore store, ChunkDirectionMask faces)
    {
        for (var from = 0; from < ChunkDirectionExtensions.Count; from++)
        {
            if ((faces & (ChunkDirectionMask)(1 << from)) == 0) continue;
            for (var to = 0; to < ChunkDirectionExtensions.Count; to++)
            {
                if ((faces & (ChunkDirectionMask)(1 << to)) != 0)
                    store.SetVisible((ChunkDirection)from, (ChunkDirection)to);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsBoundary(int x, int y, int z) =>
        x == 0 || x == Size - 1 || y == 0 || y == Size - 1 || z == 0 || z == Size - 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetIndex(int x, int y, int z) => x | (y << 4) | (z << 8);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSet(ReadOnlySpan<uint> bits, int idx) =>
        (bits[idx >> 5] & (1u << (idx & 31))) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Set(Span<uint> bits, int idx) => bits[idx >> 5] |= 1u << (idx & 31);
}
