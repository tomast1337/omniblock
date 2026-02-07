using betareborn.Blocks;
using betareborn.Entities;
using betareborn.Worlds;

namespace betareborn.Items
{
    public class ItemMinecart : Item
    {

        public int minecartType;

        public ItemMinecart(int var1, int var2) : base(var1)
        {
            maxStackSize = 1;
            minecartType = var2;
        }

        public override bool onItemUse(ItemStack var1, EntityPlayer var2, World var3, int var4, int var5, int var6, int var7)
        {
            int var8 = var3.getBlockId(var4, var5, var6);
            if (BlockRail.isRailBlock(var8))
            {
                if (!var3.multiplayerWorld)
                {
                    var3.spawnEntity(new EntityMinecart(var3, (double)((float)var4 + 0.5F), (double)((float)var5 + 0.5F), (double)((float)var6 + 0.5F), minecartType));
                }

                --var1.count;
                return true;
            }
            else
            {
                return false;
            }
        }
    }

}