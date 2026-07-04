using BetaSharp.Blocks;
using BetaSharp.Items;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockDropsAndTexturesTests
{
    [Fact]
    public void Stone_DropsCobblestone() => Assert.Equal(Block.Cobblestone.id, Block.Stone.getDroppedItemId(0));

    [Fact]
    public void GoldOre_DropsItself() => Assert.Equal(Block.GoldOre.id, Block.GoldOre.getDroppedItemId(0));

    [Fact]
    public void IronOre_DropsItself() => Assert.Equal(Block.IronOre.id, Block.IronOre.getDroppedItemId(0));

    [Fact]
    public void CoalOre_DropsCoalItem() => Assert.Equal(Item.ByName("coal").Id, Block.CoalOre.getDroppedItemId(0));

    [Fact]
    public void DiamondOre_DropsDiamondItem() => Assert.Equal(Item.ByName("diamond").Id, Block.DiamondOre.getDroppedItemId(0));

    [Fact]
    public void LapisOre_DropsDyeItemInRange()
    {
        Assert.Equal(Item.ByName("dye_powder").Id, Block.LapisOre.getDroppedItemId(0));
        for (int i = 0; i < 50; i++)
        {
            int count = Block.LapisOre.getDroppedItemCount();
            Assert.InRange(count, 4, 8);
        }
    }

    [Fact]
    public void Glowstone_DropsGlowstoneDustInRange()
    {
        Assert.Equal(Item.ByName("yellow_dust").Id, Block.Glowstone.getDroppedItemId(0));
        for (int i = 0; i < 50; i++)
        {
            int count = Block.Glowstone.getDroppedItemCount();
            Assert.InRange(count, 2, 4);
        }
    }

    [Fact]
    public void Clay_DropsFourClayItems()
    {
        Assert.Equal(Item.ByName("clay").Id, Block.Clay.getDroppedItemId(0));
        Assert.Equal(4, Block.Clay.getDroppedItemCount());
    }

    [Fact]
    public void Bookshelf_DropsNothing() => Assert.Equal(0, Block.Bookshelf.getDroppedItemCount());

    [Fact]
    public void Obsidian_DropsItself()
    {
        Assert.Equal(Block.Obsidian.id, Block.Obsidian.getDroppedItemId(0));
        Assert.Equal(1, Block.Obsidian.getDroppedItemCount());
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
