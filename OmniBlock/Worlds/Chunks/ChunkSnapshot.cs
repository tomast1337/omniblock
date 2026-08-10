using System.Buffers;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Chunks;

internal struct ChunkSnapshot : IDisposable
{
    public bool IsLit { get; private set; }

    private readonly ChunkNibbleArray _skylightMap;
    private readonly ChunkNibbleArray _blocklightMap;

    private readonly byte[] _blocks;
    private readonly ChunkNibbleArray _data;
    private bool _disposed;

    public ChunkSnapshot(Chunk toSnapshot)
    {
        _blocks = ArrayPool<byte>.Shared.Rent(toSnapshot.Blocks.Length);
        Buffer.BlockCopy(toSnapshot.Blocks, 0, _blocks, 0, toSnapshot.Blocks.Length);

        _data = MakeNibbleArray(toSnapshot.Meta.Bytes);
        _skylightMap = MakeNibbleArray(toSnapshot.SkyLight.Bytes);
        _blocklightMap = MakeNibbleArray(toSnapshot.BlockLight.Bytes);
    }

    private static ChunkNibbleArray MakeNibbleArray(byte[] toCopy)
    {
        byte[] bytes = ArrayPool<byte>.Shared.Rent(toCopy.Length);
        Buffer.BlockCopy(toCopy, 0, bytes, 0, toCopy.Length);
        return new(bytes);
    }

    public readonly int GetBlockID(int x, int y, int z)
    {
        return _blocks[ChuckFormat.GetIndex(x, y, z)];
    }

    public readonly int GetBlockMetadata(int x, int y, int z)
    {
        return _data.GetNibble(x, y, z);
    }

    public int GetBlockLightValue(int x, int y, int z, int ambientDarkness)
    {
        int skyLight = _skylightMap.GetNibble(x, y, z);
        if (skyLight > 0)
        {
            IsLit = true;
        }

        skyLight -= ambientDarkness;
        int blockLight = _blocklightMap.GetNibble(x, y, z);
        if (blockLight > skyLight)
        {
            skyLight = blockLight;
        }

        return skyLight;
    }

    /// <summary>Both channels for a cell, uncollapsed and with the time of day not yet applied.</summary>
    /// <remarks>
    ///     Sets <see cref="IsLit" /> on the same condition <see cref="GetBlockLightValue" /> does, so
    ///     a mesh built through either path agrees about whether the region has seen daylight.
    /// </remarks>
    public LightLevels GetLightLevels(int x, int y, int z)
    {
        int skyLight = _skylightMap.GetNibble(x, y, z);
        if (skyLight > 0)
        {
            IsLit = true;
        }

        return LightLevels.Of(skyLight, _blocklightMap.GetNibble(x, y, z));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ArrayPool<byte>.Shared.Return(_blocks);
        ArrayPool<byte>.Shared.Return(_data.Bytes);
        ArrayPool<byte>.Shared.Return(_skylightMap.Bytes);
        ArrayPool<byte>.Shared.Return(_blocklightMap.Bytes);
        _disposed = true;
    }
}
