using betareborn.Materials;

namespace betareborn.Blocks
{
    public class BlockCloth : Block
    {
        public BlockCloth() : base(35, 64, Material.WOOL)
        {
        }

        public override int getTexture(int var1, int var2)
        {
            if (var2 == 0)
            {
                return textureId;
            }
            else
            {
                var2 = ~(var2 & 15);
                return 113 + ((var2 & 8) >> 3) + (var2 & 7) * 16;
            }
        }

        protected override int getDroppedItemMeta(int var1)
        {
            return var1;
        }

        public static int func_21034_c(int var0)
        {
            return ~var0 & 15;
        }

        public static int func_21035_d(int var0)
        {
            return ~var0 & 15;
        }
    }

}