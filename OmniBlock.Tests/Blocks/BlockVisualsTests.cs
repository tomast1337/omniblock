using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Textures;

namespace OmniBlock.Tests.Blocks;

/// <summary>
/// Pins the <see cref="IBlockVisuals"/> extraction (wool/grass/glass/ice) against the behavior
/// of the deleted <c>BlockCloth</c>, <c>BlockGlass</c>, and <c>BlockBreakable</c> subclasses.
/// </summary>
public class BlockVisualsTests
{
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
        Assert.False(BlockRegistry.IsOpaque(BlockRegistry.Get("glass").Id));
        Assert.Equal(0, BlockRegistry.GetOpacity(BlockRegistry.Get("glass").Id));
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
        Assert.Equal(Atlases.Terrain.IndexOf("grass_block_top"), BlockRegistry.Get("grass_block").GetTexture(Side.Up));
        Assert.Equal(Atlases.Terrain.IndexOf("dirt"), BlockRegistry.Get("grass_block").GetTexture(Side.Down));
        Assert.Equal(Atlases.Terrain.IndexOf("grass_block_side"), BlockRegistry.Get("grass_block").GetTexture(Side.North));
    }

    [Fact]
    public void Grass_TopFaceUsesGrassColor()
    {
        Assert.NotEqual(0xFFFFFF, BlockRegistry.Get("grass_block").GetColorForFace(0, 1));
        Assert.Equal(0xFFFFFF, BlockRegistry.Get("grass_block").GetColorForFace(0, 0));
    }
}
