using betareborn.Blocks.Materials;
using betareborn.Entities;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockPumpkin : Block
    {

        private bool lit;

        public BlockPumpkin(int id, int textureId, bool lit) : base(id, Material.PUMPKIN)
        {
            this.textureId = textureId;
            setTickRandomly(true);
            this.lit = lit;
        }

        public override int getTexture(int side, int meta)
        {
            if (side == 1)
            {
                return textureId;
            }
            else if (side == 0)
            {
                return textureId;
            }
            else
            {
                int var3 = textureId + 1 + 16;
                if (lit)
                {
                    ++var3;
                }

                return meta == 2 && side == 2 ? var3 : (meta == 3 && side == 5 ? var3 : (meta == 0 && side == 3 ? var3 : (meta == 1 && side == 4 ? var3 : textureId + 16)));
            }
        }

        public override int getTexture(int side)
        {
            return side == 1 ? textureId : (side == 0 ? textureId : (side == 3 ? textureId + 1 + 16 : textureId + 16));
        }

        public override void onPlaced(World world, int x, int y, int z)
        {
            base.onPlaced(world, x, y, z);
        }

        public override bool canPlaceAt(World world, int x, int y, int z)
        {
            int var5 = world.getBlockId(x, y, z);
            return (var5 == 0 || Block.BLOCKS[var5].material.isReplaceable()) && world.shouldSuffocate(x, y - 1, z);
        }

        public override void onPlaced(World world, int x, int y, int z, EntityLiving placer)
        {
            int var6 = MathHelper.floor_double((double)(placer.rotationYaw * 4.0F / 360.0F) + 2.5D) & 3;
            world.setBlockMeta(x, y, z, var6);
        }
    }

}