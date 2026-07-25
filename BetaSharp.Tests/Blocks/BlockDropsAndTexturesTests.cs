using BetaSharp.Blocks;
using BetaSharp.Items;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockDropsAndTexturesTests
{
    [Fact]
    public void Stone_DropsCobblestone() => Assert.Equal(BlockRegistry.Get("cobblestone").id, BlockRegistry.Get("stone").GetDroppedItemId(0));

    [Fact]
    public void GoldOre_DropsItself() => Assert.Equal(BlockRegistry.Get("gold_ore").id, BlockRegistry.Get("gold_ore").GetDroppedItemId(0));

    [Fact]
    public void IronOre_DropsItself() => Assert.Equal(BlockRegistry.Get("iron_ore").id, BlockRegistry.Get("iron_ore").GetDroppedItemId(0));

    [Fact]
    public void CoalOre_DropsCoalItem() => Assert.Equal(Item.ByName("coal").Id, BlockRegistry.Get("coal_ore").GetDroppedItemId(0));

    [Fact]
    public void DiamondOre_DropsDiamondItem() => Assert.Equal(Item.ByName("diamond").Id, BlockRegistry.Get("diamond_ore").GetDroppedItemId(0));

    [Fact]
    public void LapisOre_DropsDyeItemInRange()
    {
        Assert.Equal(Item.ByName("dye_powder").Id, BlockRegistry.Get("lapis_ore").GetDroppedItemId(0));
        for (int i = 0; i < 50; i++)
        {
            int count = BlockRegistry.Get("lapis_ore").GetDroppedItemCount();
            Assert.InRange(count, 4, 8);
        }
    }

    [Fact]
    public void Glowstone_DropsGlowstoneDustInRange()
    {
        Assert.Equal(Item.ByName("yellow_dust").Id, BlockRegistry.Get("glowstone").GetDroppedItemId(0));
        for (int i = 0; i < 50; i++)
        {
            int count = BlockRegistry.Get("glowstone").GetDroppedItemCount();
            Assert.InRange(count, 2, 4);
        }
    }

    [Fact]
    public void Clay_DropsFourClayItems()
    {
        Assert.Equal(Item.ByName("clay").Id, BlockRegistry.Get("clay").GetDroppedItemId(0));
        Assert.Equal(4, BlockRegistry.Get("clay").GetDroppedItemCount());
    }

    [Fact]
    public void Gravel_DropsOnlyGravelOrFlint()
    {
        bool sawGravel = false;
        bool sawFlint = false;
        for (int i = 0; i < 200; i++)
        {
            int itemId = BlockRegistry.Get("gravel").GetDroppedItemId(0);
            Assert.True(itemId == BlockRegistry.Get("gravel").id || itemId == Item.ByName("flint").Id);
            sawGravel |= itemId == BlockRegistry.Get("gravel").id;
            sawFlint |= itemId == Item.ByName("flint").Id;
        }

        Assert.True(sawGravel);
        Assert.True(sawFlint);
    }

    [Fact]
    public void Bookshelf_DropsNothing() => Assert.Equal(0, BlockRegistry.Get("bookshelf").GetDroppedItemCount());

    [Fact]
    public void Obsidian_DropsItself()
    {
        Assert.Equal(BlockRegistry.Get("obsidian").id, BlockRegistry.Get("obsidian").GetDroppedItemId(0));
        Assert.Equal(1, BlockRegistry.Get("obsidian").GetDroppedItemCount());
    }

    [Fact]
    public void Sandstone_HasDistinctTopBottomSideTextures()
    {
        Assert.Equal(BlockTextures.SandstoneTop, BlockRegistry.Get("sandstone").GetTexture(Side.Up));
        Assert.Equal(BlockTextures.SandstoneBottom, BlockRegistry.Get("sandstone").GetTexture(Side.Down));
        Assert.Equal(BlockTextures.SandstoneSide, BlockRegistry.Get("sandstone").GetTexture(Side.North));
    }

    [Fact]
    public void Bookshelf_HasOakPlanksTopAndBottomTextures()
    {
        Assert.Equal(BlockTextures.OakPlanks, BlockRegistry.Get("bookshelf").GetTexture(Side.Up));
        Assert.Equal(BlockTextures.OakPlanks, BlockRegistry.Get("bookshelf").GetTexture(Side.Down));
        Assert.Equal(BlockTextures.Bookshelf, BlockRegistry.Get("bookshelf").GetTexture(Side.North));
    }

    // TryGetPrimaryLootItemId backs the middle-click "pick block" fallback: when the raw block
    // isn't obtainable/available, fall back to whatever its loot table most likely drops.
    [Fact]
    public void Stone_PrimaryLootItem_IsCobblestoneWithNoMetaConstraint()
    {
        Assert.True(BlockRegistry.Get("stone").TryGetPrimaryLootItemId(0, out int itemId, out int meta));
        Assert.Equal(BlockRegistry.Get("cobblestone").id, itemId);
        Assert.Equal(-1, meta);
    }

    [Fact]
    public void Gravel_PrimaryLootItem_IsGravelNotFlint()
    {
        Assert.True(BlockRegistry.Get("gravel").TryGetPrimaryLootItemId(0, out int itemId, out _));
        Assert.Equal(BlockRegistry.Get("gravel").id, itemId);
    }

    [Fact]
    public void DoubleSlab_PrimaryLootItem_PreservesBlockMetaOnSlabItem()
    {
        Assert.True(BlockRegistry.Get("double_slab").TryGetPrimaryLootItemId(3, out int itemId, out int meta));
        Assert.Equal(BlockRegistry.Get("slab").id, itemId);
        Assert.Equal(3, meta);
    }

    [Fact]
    public void GrassBlock_PrimaryLootItem_IsDirtWithNoMetaConstraint()
    {
        Assert.True(BlockRegistry.Get("grass_block").TryGetPrimaryLootItemId(0, out int itemId, out int meta));
        Assert.Equal(BlockRegistry.Get("dirt").id, itemId);
        Assert.Equal(-1, meta);
    }

    [Fact]
    public void Bedrock_HasNoLootTable_TryGetPrimaryLootItemIdReturnsFalse()
    {
        Assert.False(BlockRegistry.Get("bedrock").TryGetPrimaryLootItemId(0, out int itemId, out int meta));
        Assert.Equal(0, itemId);
        Assert.Equal(-1, meta);
    }
}
