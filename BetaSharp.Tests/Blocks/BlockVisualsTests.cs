using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;

namespace BetaSharp.Tests.Blocks;

/// <summary>
/// Pins the <see cref="IBlockVisuals"/> extraction (wool/grass/glass/ice) against the behavior
/// of the deleted <c>BlockCloth</c>, <c>BlockGlass</c>, and <c>BlockBreakable</c> subclasses.
/// </summary>
public class BlockVisualsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(15)]
    public void Wool_TextureMatchesOldPaletteFormula(int meta)
    {
        int expected;
        if (meta == 0)
        {
            expected = 64;
        }
        else
        {
            int inverted = ~(meta & 15);
            expected = BlockTextures.WoolColoredPaletteBase + ((inverted & 8) >> 3) + (inverted & 7) * 16;
        }

        Assert.Equal(expected, BlockRegistry.Get("wool").GetTexture(Side.North, meta));
    }

    [Fact]
    public void Wool_MetaRoundTripsBetweenItemAndBlock()
    {
        for (int itemMeta = 0; itemMeta < 16; itemMeta++)
        {
            Assert.Equal(itemMeta, ClothVisualBehavior.GetItemMeta(ClothVisualBehavior.GetBlockMeta(itemMeta)));
        }
    }

    [Fact]
    public void Wool_HasSixteenAliases()
    {
        Assert.Equal(16, BlockRegistry.Get("wool").GetBlockAlias.Count);
        Assert.Contains("whiteWool:0", BlockRegistry.Get("wool").GetBlockAlias);
        Assert.Contains("blackWool:15", BlockRegistry.Get("wool").GetBlockAlias);
    }

    [Fact]
    public void FlattenedBlocks_KeepTheirIdentity()
    {
        // Glass: plain Block composed with GlassVisualBehavior, non-opaque, drops nothing.
        Assert.IsType<Block>(BlockRegistry.Get("glass"));
        Assert.IsType<GlassVisualBehavior>(BlockRegistry.Get("glass").Visuals);
        Assert.False(BlockRegistry.Get("glass").IsOpaque);
        Assert.False(Block.BlocksOpaque[BlockRegistry.Get("glass").id]);
        Assert.Equal(0, Block.BlockLightOpacity[BlockRegistry.Get("glass").id]);
        Assert.Equal(0, BlockRegistry.Get("glass").GetDroppedItemCount());

        // Wool: plain Block composed with ClothVisualBehavior.
        Assert.IsType<Block>(BlockRegistry.Get("wool"));
        Assert.IsType<ClothVisualBehavior>(BlockRegistry.Get("wool").Visuals);
        Assert.True(BlockRegistry.Get("wool").IsOpaque);

        // Grass keeps its subclass (tick spreading) but visuals moved to the behavior.
        Assert.IsType<GrassVisualBehavior>(BlockRegistry.Get("grass_block").Visuals);

        // Ice/portal kept non-opacity after losing BlockBreakable.
        Assert.False(BlockRegistry.Get("ice").IsOpaque);
        Assert.False(BlockRegistry.Get("nether_portal").IsOpaque);
    }

    [Fact]
    public void Grass_TopAndBottomTexturesAreDeclarative()
    {
        Assert.Equal(BlockTextures.GrassTop, BlockRegistry.Get("grass_block").GetTexture(Side.Up));
        Assert.Equal(BlockTextures.Dirt, BlockRegistry.Get("grass_block").GetTexture(Side.Down));
        Assert.Equal(BlockTextures.GrassSide, BlockRegistry.Get("grass_block").GetTexture(Side.North));
    }

    [Fact]
    public void Grass_TopFaceUsesGrassColor()
    {
        Assert.NotEqual(0xFFFFFF, BlockRegistry.Get("grass_block").getColorForFace(0, 1));
        Assert.Equal(0xFFFFFF, BlockRegistry.Get("grass_block").getColorForFace(0, 0));
    }
}
