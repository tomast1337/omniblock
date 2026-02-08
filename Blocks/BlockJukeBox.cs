using betareborn.Entities;
using betareborn.Items;
using betareborn.Worlds;
using betareborn.Blocks.BlockEntities;
using betareborn.Blocks.Materials;

namespace betareborn.Blocks
{
    public class BlockJukeBox : BlockWithEntity
    {

        public BlockJukeBox(int id, int textureId) : base(id, textureId, Material.WOOD)
        {
        }

        public override int getTexture(int side)
        {
            return textureId + (side == 1 ? 1 : 0);
        }

        public override bool onUse(World world, int x, int y, int z, EntityPlayer player)
        {
            if (world.getBlockMeta(x, y, z) == 0)
            {
                return false;
            }
            else
            {
                tryEjectRecord(world, x, y, z);
                return true;
            }
        }

        public void insertRecord(World world, int x, int y, int z, int id)
        {
            if (!world.isRemote)
            {
                BlockEntityRecordPlayer var6 = (BlockEntityRecordPlayer)world.getBlockEntity(x, y, z);
                var6.recordId = id;
                var6.markDirty();
                world.setBlockMeta(x, y, z, 1);
            }
        }

        public void tryEjectRecord(World world, int x, int y, int z)
        {
            if (!world.isRemote)
            {
                BlockEntityRecordPlayer var5 = (BlockEntityRecordPlayer)world.getBlockEntity(x, y, z);
                int var6 = var5.recordId;
                if (var6 != 0)
                {
                    world.worldEvent(1005, x, y, z, 0);
                    world.playRecord((String)null, x, y, z);
                    var5.recordId = 0;
                    var5.markDirty();
                    world.setBlockMeta(x, y, z, 0);
                    float var8 = 0.7F;
                    double var9 = (double)(world.random.nextFloat() * var8) + (double)(1.0F - var8) * 0.5D;
                    double var11 = (double)(world.random.nextFloat() * var8) + (double)(1.0F - var8) * 0.2D + 0.6D;
                    double var13 = (double)(world.random.nextFloat() * var8) + (double)(1.0F - var8) * 0.5D;
                    EntityItem var15 = new EntityItem(world, (double)x + var9, (double)y + var11, (double)z + var13, new ItemStack(var6, 1, 0));
                    var15.delayBeforeCanPickup = 10;
                    world.spawnEntity(var15);
                }
            }
        }

        public override void onBreak(World world, int x, int y, int z)
        {
            tryEjectRecord(world, x, y, z);
            base.onBreak(world, x, y, z);
        }

        public override void dropStacks(World world, int x, int y, int z, int meta, float luck)
        {
            if (!world.isRemote)
            {
                base.dropStacks(world, x, y, z, meta, luck);
            }
        }

        protected override BlockEntity getBlockEntity()
        {
            return new BlockEntityRecordPlayer();
        }
    }

}