using betareborn.Blocks;
using betareborn.Entities;
using betareborn.Util.Maths;
using betareborn.Worlds;

namespace betareborn.Items
{
    public class ItemBed : Item
    {

        public ItemBed(int var1) : base(var1)
        {
        }

        public override bool onItemUse(ItemStack var1, EntityPlayer var2, World var3, int var4, int var5, int var6, int var7)
        {
            if (var7 != 1)
            {
                return false;
            }
            else
            {
                ++var5;
                BlockBed var8 = (BlockBed)Block.BED;
                int var9 = MathHelper.floor_double((double)(var2.rotationYaw * 4.0F / 360.0F) + 0.5D) & 3;
                sbyte var10 = 0;
                sbyte var11 = 0;
                if (var9 == 0)
                {
                    var11 = 1;
                }

                if (var9 == 1)
                {
                    var10 = -1;
                }

                if (var9 == 2)
                {
                    var11 = -1;
                }

                if (var9 == 3)
                {
                    var10 = 1;
                }

                if (var3.isAir(var4, var5, var6) && var3.isAir(var4 + var10, var5, var6 + var11) && var3.shouldSuffocate(var4, var5 - 1, var6) && var3.shouldSuffocate(var4 + var10, var5 - 1, var6 + var11))
                {
                    var3.setBlockAndMetadataWithNotify(var4, var5, var6, var8.id, var9);
                    var3.setBlockAndMetadataWithNotify(var4 + var10, var5, var6 + var11, var8.id, var9 + 8);
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

}