using OmniBlock.Worlds.Lod;

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
    public void Seals_only_complete_no_skylight_air_spans_below_the_ceiling()
    {
        var source = TerrainLodColumn.Create(128,
        [
            new TerrainLodColumnSpan(0, 16, Stone, 0, 0),
            new TerrainLodColumnSpan(16, 8, TerrainLodMaterial.Air, 0, 0),
            new TerrainLodColumnSpan(24, 16, Stone, 0, 0),
            new TerrainLodColumnSpan(40, 8, TerrainLodMaterial.Air, 0, 15),
            new TerrainLodColumnSpan(48, 8, Stone, 0, 0),
            new TerrainLodColumnSpan(56, 16, TerrainLodMaterial.Air, 0, 0),
            new TerrainLodColumnSpan(72, 56, TerrainLodMaterial.Air, 0, 15)
        ]);

        var culled = TerrainLodCaveCuller.SealUndergroundAir(source, ceilingY: 64);

        Assert.False(culled.At(20).IsAir);
        Assert.True(culled.At(44).IsAir); // skylight proves an opening
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
}
