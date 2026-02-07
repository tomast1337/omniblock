using betareborn.Blocks;
using betareborn.Materials;

namespace betareborn.Items
{
    public class ItemPickaxe : ItemTool
    {

        private static Block[] blocksEffectiveAgainst = new Block[] { Block.COBBLESTONE, Block.stairDouble, Block.stairSingle, Block.STONE, Block.SANDSTONE, Block.cobblestoneMossy, Block.IRON_ORE, Block.blockSteel, Block.COAL_ORE, Block.blockGold, Block.GOLD_ORE, Block.oreDiamond, Block.blockDiamond, Block.ice, Block.netherrack, Block.LAPIS_ORE, Block.LAPIS_BLOCK };

        public ItemPickaxe(int var1, EnumToolMaterial var2) : base(var1, 2, var2, blocksEffectiveAgainst)
        {
        }

        public override bool canHarvestBlock(Block var1)
        {
            return var1 == Block.obsidian ? toolMaterial.getHarvestLevel() == 3 : (var1 != Block.blockDiamond && var1 != Block.oreDiamond ? (var1 != Block.blockGold && var1 != Block.GOLD_ORE ? (var1 != Block.blockSteel && var1 != Block.IRON_ORE ? (var1 != Block.LAPIS_BLOCK && var1 != Block.LAPIS_ORE ? (var1 != Block.oreRedstone && var1 != Block.oreRedstoneGlowing ? (var1.blockMaterial == Material.STONE ? true : var1.blockMaterial == Material.METAL) : toolMaterial.getHarvestLevel() >= 2) : toolMaterial.getHarvestLevel() >= 1) : toolMaterial.getHarvestLevel() >= 1) : toolMaterial.getHarvestLevel() >= 2) : toolMaterial.getHarvestLevel() >= 2);
        }
    }

}