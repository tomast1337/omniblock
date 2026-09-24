using OmniBlock.Worlds.Lod;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Chunks;

namespace OmniBlock.Tests.Worlds;

public sealed class TerrainLodCaveCullerTests
{
    private static readonly TerrainLodMaterial Stone = new(
        new ResourceLocation(Namespace.OmniBlock, "stone"),
        0,
        TerrainLodGeometryClass.Opaque,
        true,
        0x777777);

    [Fact]
    public void Seals_only_complete_unlit_air_spans_below_the_ceiling()
    {
        var source = TerrainLodColumn.Create(128,
        [
            new TerrainLodColumnSpan(0, 16, Stone, 0, 0),
            new TerrainLodColumnSpan(16, 8, TerrainLodMaterial.Air, 0, 0),
            new TerrainLodColumnSpan(24, 16, Stone, 0, 0),
            new TerrainLodColumnSpan(40, 8, TerrainLodMaterial.Air, 0, 15),
            new TerrainLodColumnSpan(48, 4, Stone, 0, 0),
            new TerrainLodColumnSpan(52, 4, TerrainLodMaterial.Air, 8, 0),
            new TerrainLodColumnSpan(56, 16, TerrainLodMaterial.Air, 0, 0),
            new TerrainLodColumnSpan(72, 56, TerrainLodMaterial.Air, 0, 15)
        ]);

        var culled = TerrainLodCaveCuller.SealUndergroundAir(source, ceilingY: 64);

        Assert.False(culled.At(20).IsAir);
        Assert.True(culled.At(44).IsAir); // skylight proves an opening
        Assert.True(culled.At(54).IsAir); // block light also makes this air visible
        Assert.True(culled.At(60).IsAir); // interval crosses the configured ceiling
        Assert.Same(source, TerrainLodCaveCuller.SealUndergroundAir(source, ceilingY: 0));
    }

    [Fact]
    public void Leaves_canonical_source_unchanged_and_canonicalizes_the_copy()
    {
        var source = TerrainLodColumn.Create(32,
        [
            new TerrainLodColumnSpan(0, 8, Stone, 0, 0),
            new TerrainLodColumnSpan(8, 8, TerrainLodMaterial.Air, 0, 0),
            new TerrainLodColumnSpan(16, 8, Stone, 0, 0),
            new TerrainLodColumnSpan(24, 8, TerrainLodMaterial.Air, 0, 15)
        ]);

        var culled = TerrainLodCaveCuller.SealUndergroundAir(source, ceilingY: 24);

        Assert.True(source.At(12).IsAir);
        Assert.Equal(2, culled.Spans.Count);
        Assert.Equal(24, culled.Spans[0].Height);
        Assert.False(culled.Spans[0].IsAir);
    }

    [Fact]
    public void Preserves_only_the_bounded_dark_air_connected_to_a_daylit_cave_mouth()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stone = checked((byte)world.Content.Blocks.Get("omniblock:stone").Id);
        var blocks = new byte[ChuckFormat.ChunkSize];
        var metadata = new byte[blocks.Length];
        var sky = new ChunkNibbleArray(ChuckFormat.ChunkSize);
        var block = new ChunkNibbleArray(ChuckFormat.ChunkSize);
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = 0; y < ChuckFormat.ChunkHeight; y++)
        {
            var tunnel = z == 8 && x is >= 1 and <= 7 && y is >= 16 and < 24;
            var mouth = z == 8 && x == 1 && y is >= 24 and < 32;
            blocks[ChuckFormat.GetIndex(x, y, z)] = y < 32 && !tunnel && !mouth
                ? stone : (byte)0;
            if (y >= 32 || mouth) sky.SetNibble(x, y, z, 15);
        }
        var source = new TerrainLodSourceSnapshot(
            0, 0, 16, ChuckFormat.ChunkHeight, 16, blocks, metadata, 1,
            new TerrainLodLightingSnapshot(0, 0, 1, sky.Bytes, block.Bytes, true));
        var tile = TerrainLodColumnTile.BuildLeaf(source, materials);
        var exposure = TerrainLodCaveCuller.FindExposedAir(tile);
        Assert.Same(TerrainLodCaveCuller.GetDefaultExposure(tile),
            TerrainLodCaveCuller.GetDefaultExposure(tile));

        var near = TerrainLodCaveCuller.SealUndergroundAir(
            tile[5, 8], 32, exposure.ForColumn(5, 8));
        var far = TerrainLodCaveCuller.SealUndergroundAir(
            tile[6, 8], 32, exposure.ForColumn(6, 8));

        Assert.True(near.At(20).IsAir); // four horizontal steps from the shaft
        Assert.False(far.At(20).IsAir); // fifth step stays sealed
        Assert.True(tile[6, 8].At(20).IsAir); // canonical source is untouched
    }
}
