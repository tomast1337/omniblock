using betareborn.Entities;
using betareborn.Items;
using betareborn.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockTNT : Block
    {
        public BlockTNT(int id, int textureId) : base(id, textureId, Material.TNT)
        {
        }

        public override int getTexture(int side)
        {
            return side == 0 ? textureId + 2 : (side == 1 ? textureId + 1 : textureId);
        }

        public override void onPlaced(World world, int x, int y, int z)
        {
            base.onPlaced(world, x, y, z);
            if (world.isPowered(x, y, z))
            {
                onMetadataChange(world, x, y, z, 1);
                world.setBlockWithNotify(x, y, z, 0);
            }

        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            if (id > 0 && Block.BLOCKS[id].canEmitRedstonePower() && world.isPowered(x, y, z))
            {
                onMetadataChange(world, x, y, z, 1);
                world.setBlockWithNotify(x, y, z, 0);
            }

        }

        public override int getDroppedItemCount(java.util.Random random)
        {
            return 0;
        }

        public override void onDestroyedByExplosion(World world, int x, int y, int z)
        {
            EntityTNTPrimed var5 = new EntityTNTPrimed(world, (double)((float)x + 0.5F), (double)((float)y + 0.5F), (double)((float)z + 0.5F));
            var5.fuse = world.random.nextInt(var5.fuse / 4) + var5.fuse / 8;
            world.spawnEntity(var5);
        }

        public override void onMetadataChange(World world, int x, int y, int z, int meta)
        {
            if (!world.isRemote)
            {
                if ((meta & 1) == 0)
                {
                    dropStack(world, x, y, z, new ItemStack(Block.TNT.id, 1, 0));
                }
                else
                {
                    EntityTNTPrimed var6 = new EntityTNTPrimed(world, (double)((float)x + 0.5F), (double)((float)y + 0.5F), (double)((float)z + 0.5F));
                    world.spawnEntity(var6);
                    world.playSoundAtEntity(var6, "random.fuse", 1.0F, 1.0F);
                }

            }
        }

        public override void onBlockBreakStart(World world, int x, int y, int z, EntityPlayer player)
        {
            if (player.getCurrentEquippedItem() != null && player.getCurrentEquippedItem().itemID == Item.flintAndSteel.id)
            {
                world.setBlockMetadata(x, y, z, 1);
            }

            base.onBlockBreakStart(world, x, y, z, player);
        }

        public override bool onUse(World world, int x, int y, int z, EntityPlayer player)
        {
            return base.onUse(world, x, y, z, player);
        }
    }

}