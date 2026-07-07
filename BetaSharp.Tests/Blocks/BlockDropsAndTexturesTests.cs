using BetaSharp.Blocks;
using BetaSharp.Items;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockDropsAndTexturesTests
{
    [Fact]
    public void Stone_DropsCobblestone() => Assert.Equal(Block.Cobblestone.Id, Block.Stone.GetDroppedItemId(0));

    [Fact]
    public void GoldOre_DropsItself() => Assert.Equal(Block.GoldOre.Id, Block.GoldOre.GetDroppedItemId(0));

    [Fact]
    public void IronOre_DropsItself() => Assert.Equal(Block.IronOre.Id, Block.IronOre.GetDroppedItemId(0));

    [Fact]
    public void CoalOre_DropsCoalItem() => Assert.Equal(Item.ByName("coal").Id, Block.CoalOre.GetDroppedItemId(0));

    [Fact]
    public void DiamondOre_DropsDiamondItem() => Assert.Equal(Item.ByName("diamond").Id, Block.DiamondOre.GetDroppedItemId(0));

    [Fact]
    public void LapisOre_DropsDyeItemInRange()
    {
        Assert.Equal(Item.ByName("dye_powder").Id, Block.LapisOre.GetDroppedItemId(0));
        for (int i = 0; i < 50; i++)
        {
            int count = Block.LapisOre.GetDroppedItemCount();
            Assert.InRange(count, 4, 8);
        }
    }

    [Fact]
    public void Glowstone_DropsGlowstoneDustInRange()
    {
        Assert.Equal(Item.ByName("yellow_dust").Id, Block.Glowstone.GetDroppedItemId(0));
        for (int i = 0; i < 50; i++)
        {
            int count = Block.Glowstone.GetDroppedItemCount();
            Assert.InRange(count, 2, 4);
        }
    }

    [Fact]
    public void Clay_DropsFourClayItems()
    {
        Assert.Equal(Item.ByName("clay").Id, Block.Clay.GetDroppedItemId(0));
        Assert.Equal(4, Block.Clay.GetDroppedItemCount());
    }

    [Fact]
    public void Gravel_DropsOnlyGravelOrFlint()
    {
        bool sawGravel = false;
        bool sawFlint = false;
        for (int i = 0; i < 200; i++)
        {
            int itemId = Block.Gravel.GetDroppedItemId(0);
            Assert.True(itemId == Block.Gravel.Id || itemId == Item.ByName("flint").Id);
            sawGravel |= itemId == Block.Gravel.Id;
            sawFlint |= itemId == Item.ByName("flint").Id;
        }

        Assert.True(sawGravel);
        Assert.True(sawFlint);
    }

    [Fact]
    public void Bookshelf_DropsNothing() => Assert.Equal(0, Block.Bookshelf.GetDroppedItemCount());

    [Fact]
    public void Obsidian_DropsItself()
    {
        Assert.Equal(Block.Obsidian.Id, Block.Obsidian.GetDroppedItemId(0));
        Assert.Equal(1, Block.Obsidian.GetDroppedItemCount());
    }

    [Fact]
    public void Sandstone_HasDistinctTopBottomSideTextures()
    {
        Assert.Equal(BlockTextures.SandstoneTop, Block.Sandstone.GetTexture(Side.Up));
        Assert.Equal(BlockTextures.SandstoneBottom, Block.Sandstone.GetTexture(Side.Down));
        Assert.Equal(BlockTextures.SandstoneSide, Block.Sandstone.GetTexture(Side.North));
    }

    [Fact]
    public void Bookshelf_HasOakPlanksTopAndBottomTextures()
    {
        Assert.Equal(BlockTextures.OakPlanks, Block.Bookshelf.GetTexture(Side.Up));
        Assert.Equal(BlockTextures.OakPlanks, Block.Bookshelf.GetTexture(Side.Down));
        Assert.Equal(BlockTextures.Bookshelf, Block.Bookshelf.GetTexture(Side.North));
    }
}
