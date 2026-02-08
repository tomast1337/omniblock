using betareborn.Items;

namespace betareborn.Blocks
{
    public class BlockTallGrass : BlockPlant
    {
        public BlockTallGrass(int i, int j) : base(i, j)
        {
            float var3 = 0.4F;
            setBoundingBox(0.5F - var3, 0.0F, 0.5F - var3, 0.5F + var3, 0.8F, 0.5F + var3);
        }

        public override int getTexture(int side, int meta)
        {
            return meta == 1 ? textureId : (meta == 2 ? textureId + 16 + 1 : (meta == 0 ? textureId + 16 : textureId));
        }

        public override int getColorMultiplier(BlockView blockView, int x, int y, int z)
        {
            int var5 = blockView.getBlockMeta(x, y, z);
            if (var5 == 0)
            {
                return 16777215;
            }
            else
            {
                long var6 = (long)(x * 3129871 + z * 6129781 + y);
                var6 = var6 * var6 * 42317861L + var6 * 11L;
                x = (int)((long)x + (var6 >> 14 & 31L));
                y = (int)((long)y + (var6 >> 19 & 31L));
                z = (int)((long)z + (var6 >> 24 & 31L));
                blockView.getBiomeSource().getBiomesInArea(x, z, 1, 1);
                double var8 = blockView.getBiomeSource().temperatureMap[0];
                double var10 = blockView.getBiomeSource().downfallMap[0];
                return GrassColors.getColor(var8, var10);
            }
        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return random.nextInt(8) == 0 ? Item.seeds.id : -1;
        }
    }

}