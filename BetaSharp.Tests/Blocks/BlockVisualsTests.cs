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

        Assert.Equal(expected, Block.Wool.GetTexture(Side.North, meta));
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
        Assert.Equal(16, Block.Wool.GetBlockAlias.Count);
        Assert.Contains("whiteWool:0", Block.Wool.GetBlockAlias);
        Assert.Contains("blackWool:15", Block.Wool.GetBlockAlias);
    }

    [Fact]
    public void FlattenedBlocks_KeepTheirIdentity()
    {
        // Glass: plain Block composed with GlassVisualBehavior, non-opaque, drops nothing.
        Assert.IsType<Block>(Block.Glass);
        Assert.IsType<GlassVisualBehavior>(Block.Glass.Visuals);
        Assert.False(Block.Glass.IsOpaque());
        Assert.False(Block.BlocksOpaque[Block.Glass.Id]);
        Assert.Equal(0, Block.BlockLightOpacity[Block.Glass.Id]);
        Assert.Equal(0, Block.Glass.GetDroppedItemCount());

        // Wool: plain Block composed with ClothVisualBehavior.
        Assert.IsType<Block>(Block.Wool);
        Assert.IsType<ClothVisualBehavior>(Block.Wool.Visuals);
        Assert.True(Block.Wool.IsOpaque());

        // Grass keeps its subclass (tick spreading) but visuals moved to the behavior.
        Assert.IsType<GrassVisualBehavior>(Block.GrassBlock.Visuals);

        // Ice/portal kept non-opacity after losing BlockBreakable.
        Assert.False(Block.Ice.IsOpaque());
        Assert.False(Block.NetherPortal.IsOpaque());
    }

    [Fact]
    public void Grass_TopAndBottomTexturesAreDeclarative()
    {
        Assert.Equal(BlockTextures.GrassTop, Block.GrassBlock.GetTexture(Side.Up));
        Assert.Equal(BlockTextures.Dirt, Block.GrassBlock.GetTexture(Side.Down));
        Assert.Equal(BlockTextures.GrassSide, Block.GrassBlock.GetTexture(Side.North));
    }

    [Fact]
    public void Grass_TopFaceUsesGrassColor()
    {
        Assert.NotEqual(0xFFFFFF, Block.GrassBlock.GetColorForFace(0, 1));
        Assert.Equal(0xFFFFFF, Block.GrassBlock.GetColorForFace(0, 0));
    }
}
