using OmniBlock.Blocks;
using OmniBlock.Textures;
using OmniBlock.Items;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockDropsAndTexturesTests
{
    [Fact]
    public void Stone_DropsCobblestone() => Assert.Equal(TestBlocks.Get("cobblestone").Id, TestBlocks.Get("stone").GetDroppedItemId(0));

    [Fact]
    public void GoldOre_DropsItself() => Assert.Equal(TestBlocks.Get("gold_ore").Id, TestBlocks.Get("gold_ore").GetDroppedItemId(0));

    [Fact]
    public void IronOre_DropsItself() => Assert.Equal(TestBlocks.Get("iron_ore").Id, TestBlocks.Get("iron_ore").GetDroppedItemId(0));

    [Fact]
    public void CoalOre_DropsCoalItem() => Assert.Equal(ContentRuntime.Current.Items.Get("omniblock:coal").Id, TestBlocks.Get("coal_ore").GetDroppedItemId(0));

    [Fact]
    public void DiamondOre_DropsDiamondItem() => Assert.Equal(ContentRuntime.Current.Items.Get("omniblock:diamond").Id, TestBlocks.Get("diamond_ore").GetDroppedItemId(0));

    [Fact]
    public void LapisOre_DropsDyeItemInRange()
    {
        Assert.Equal(ContentRuntime.Current.Items.Get("omniblock:dye_powder").Id, TestBlocks.Get("lapis_ore").GetDroppedItemId(0));
        for (int i = 0; i < 50; i++)
        {
            int count = TestBlocks.Get("lapis_ore").GetDroppedItemCount();
            Assert.InRange(count, 4, 8);
        }
    }

    [Fact]
    public void Glowstone_DropsGlowstoneDustInRange()
    {
        Assert.Equal(ContentRuntime.Current.Items.Get("omniblock:yellow_dust").Id, TestBlocks.Get("glowstone").GetDroppedItemId(0));
        for (int i = 0; i < 50; i++)
        {
            int count = TestBlocks.Get("glowstone").GetDroppedItemCount();
            Assert.InRange(count, 2, 4);
        }
    }

    [Fact]
    public void Clay_DropsFourClayItems()
    {
        Assert.Equal(ContentRuntime.Current.Items.Get("omniblock:clay").Id, TestBlocks.Get("clay").GetDroppedItemId(0));
        Assert.Equal(4, TestBlocks.Get("clay").GetDroppedItemCount());
    }

    [Fact]
    public void Gravel_DropsOnlyGravelOrFlint()
    {
        bool sawGravel = false;
        bool sawFlint = false;
        for (int i = 0; i < 200; i++)
        {
            int itemId = TestBlocks.Get("gravel").GetDroppedItemId(0);
            Assert.True(itemId == TestBlocks.Get("gravel").Id || itemId == ContentRuntime.Current.Items.Get("omniblock:flint").Id);
            sawGravel |= itemId == TestBlocks.Get("gravel").Id;
            sawFlint |= itemId == ContentRuntime.Current.Items.Get("omniblock:flint").Id;
        }

        Assert.True(sawGravel);
        Assert.True(sawFlint);
    }

    [Fact]
    public void Bookshelf_DropsNothing() => Assert.Equal(0, TestBlocks.Get("bookshelf").GetDroppedItemCount());

    [Fact]
    public void Obsidian_DropsItself()
    {
        Assert.Equal(TestBlocks.Get("obsidian").Id, TestBlocks.Get("obsidian").GetDroppedItemId(0));
        Assert.Equal(1, TestBlocks.Get("obsidian").GetDroppedItemCount());
    }

    [Fact]
    public void Sandstone_HasDistinctTopBottomSideTextures()
    {
        Assert.Equal(Atlases.Terrain.IndexOf("sandstone_top"), TestBlocks.Get("sandstone").GetTexture(Side.Up));
        Assert.Equal(Atlases.Terrain.IndexOf("sandstone_bottom"), TestBlocks.Get("sandstone").GetTexture(Side.Down));
        Assert.Equal(Atlases.Terrain.IndexOf("sandstone_side"), TestBlocks.Get("sandstone").GetTexture(Side.North));
    }

    [Fact]
    public void Bookshelf_HasOakPlanksTopAndBottomTextures()
    {
        Assert.Equal(Atlases.Terrain.IndexOf("wooden_planks"), TestBlocks.Get("bookshelf").GetTexture(Side.Up));
        Assert.Equal(Atlases.Terrain.IndexOf("wooden_planks"), TestBlocks.Get("bookshelf").GetTexture(Side.Down));
        Assert.Equal(Atlases.Terrain.IndexOf("bookshelf"), TestBlocks.Get("bookshelf").GetTexture(Side.North));
    }

    [Fact]
    public void Stone_PickBlockItem_BackupIsCobblestoneWithNoMetaConstraint()
    {
        (int primaryMeta, int backupId, int backupMeta) = TestBlocks.Get("stone").GetPickBlockItem(0);
        Assert.Equal(0, primaryMeta);
        Assert.Equal(TestBlocks.Get("cobblestone").Id, backupId);
        Assert.Equal(-1, backupMeta);
    }

    [Fact]
    public void Gravel_PickBlockItem_BackupIsGravelNotFlint()
    {
        (_, int backupId, _) = TestBlocks.Get("gravel").GetPickBlockItem(0);
        Assert.Equal(TestBlocks.Get("gravel").Id, backupId);
    }

    [Fact]
    public void DoubleSlab_PickBlockItem_BackupPreservesBlockMetaOnSlabItem()
    {
        (_, int backupId, int backupMeta) = TestBlocks.Get("double_slab").GetPickBlockItem(3);
        Assert.Equal(TestBlocks.Get("slab").Id, backupId);
        Assert.Equal(3, backupMeta);
    }

    [Fact]
    public void GrassBlock_PickBlockItem_BackupIsDirtWithNoMetaConstraint()
    {
        (_, int backupId, int backupMeta) = TestBlocks.Get("grass_block").GetPickBlockItem(0);
        Assert.Equal(TestBlocks.Get("dirt").Id, backupId);
        Assert.Equal(-1, backupMeta);
    }

    [Fact]
    public void Bedrock_HasNoLootTable_PickBlockItemHasNoBackup()
    {
        (int primaryMeta, int backupId, int backupMeta) = TestBlocks.Get("bedrock").GetPickBlockItem(0);
        Assert.Equal(0, primaryMeta);
        Assert.Equal(0, backupId);
        Assert.Equal(-1, backupMeta);
    }

    [Fact]
    public void Leaves_PickBlockItem_MasksDecayBitsAndBacksUpToSapling()
    {
        const int oakWithDecayAndPersistentBits = 0b1101; // oak (bits 0-1 = 01) + check-decay (4) + persistent (8)
        (int primaryMeta, int backupId, int backupMeta) = TestBlocks.Get("leaves").GetPickBlockItem(oakWithDecayAndPersistentBits);
        Assert.Equal(1, primaryMeta);
        Assert.Equal(TestBlocks.Get("sapling").Id, backupId);
        Assert.Equal(1, backupMeta);
    }
}
