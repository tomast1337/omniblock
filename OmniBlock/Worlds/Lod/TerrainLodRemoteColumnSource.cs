using OmniBlock.Blocks;
using OmniBlock.Worlds.Chunks;

namespace OmniBlock.Worlds.Lod;

/// <summary>Expands a lossless LOD leaf for local mesh replacement, never into a gameplay chunk.</summary>
public static class TerrainLodRemoteColumnSource
{
    public static TerrainLodSourceSnapshot Expand(
        TerrainLodColumnTile tile, IBlockRuntimeView blocks, bool hasSkyLight)
    {
        if (tile.Key.Level != 0 || tile.HorizontalSampleLevel != 0 ||
            tile.WorldHeight != ChuckFormat.WorldHeight ||
            tile.LeafTerrainRevision is not { } revision)
            throw new ArgumentException("A refresh requires a full-height, revisioned, block-scale leaf.", nameof(tile));
        var ids = new byte[ChuckFormat.ChunkSize];
        var metadata = new byte[ids.Length];
        var sky = new ChunkNibbleArray(new byte[ids.Length / 2]);
        var light = new ChunkNibbleArray(new byte[ids.Length / 2]);
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = 0; y < ChuckFormat.WorldHeight; y++)
        {
            var span = tile[x, z].At(y);
            var index = ChuckFormat.GetIndex(x, y, z);
            ids[index] = span.Material.IsAir ? (byte)0 : checked((byte)blocks.Get(span.Material.BlockId).Id);
            metadata[index] = span.Material.Metadata;
            sky.SetNibble(x, y, z, span.SkyLight);
            light.SetNibble(x, y, z, span.BlockLight);
        }
        return new TerrainLodSourceSnapshot(tile.Key.X, tile.Key.Z, 16, ChuckFormat.WorldHeight, 16,
            ids, metadata, revision,
            new TerrainLodLightingSnapshot(tile.Key.X, tile.Key.Z, revision, sky.Bytes, light.Bytes, hasSkyLight),
            tile.Climate);
    }
}
