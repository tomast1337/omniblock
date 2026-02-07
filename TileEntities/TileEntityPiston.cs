using betareborn.Blocks;
using betareborn.Entities;
using betareborn.NBT;

namespace betareborn.TileEntities
{
    public class TileEntityPiston : TileEntity
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(TileEntityPiston).TypeHandle);

        private int pushedBlockId;
        private int pushedBlockData;
        private int facing;
        private bool extending;
        private readonly bool source;
        private float lastProgess;
        private float progress;
        private static readonly List<Entity> pushedEntities = [];

        public TileEntityPiston()
        {
        }

        public TileEntityPiston(int pushedBlockId, int pushedBlockData, int facing, bool extending, bool source)
        {
            this.pushedBlockId = pushedBlockId;
            this.pushedBlockData = pushedBlockData;
            this.facing = facing;
            this.extending = extending;
            this.source = source;
        }

        public int getPushedBlockId()
        {
            return pushedBlockId;
        }

        public override int getPushedBlockData()
        {
            return pushedBlockData;
        }

        public bool isExtending()
        {
            return extending;
        }

        public int getFacing()
        {
            return facing;
        }

        public bool isSource()
        {
            return source;
        }

        public float getProgress(float tickDelta)
        {
            if (tickDelta > 1.0F)
            {
                tickDelta = 1.0F;
            }

            return progress + (lastProgess - progress) * tickDelta;
        }

        public float getRenderOffsetX(float tickDelta)
        {
            return extending ? (getProgress(tickDelta) - 1.0F) * (float)PistonBlockTextures.field_31056_b[facing] : (1.0F - getProgress(tickDelta)) * (float)PistonBlockTextures.field_31056_b[facing];
        }

        public float getRenderOffsetY(float tickDelta)
        {
            return extending ? (getProgress(tickDelta) - 1.0F) * (float)PistonBlockTextures.field_31059_c[facing] : (1.0F - getProgress(tickDelta)) * (float)PistonBlockTextures.field_31059_c[facing];
        }

        public float getRenderOffsetZ(float tickDelta)
        {
            return extending ? (getProgress(tickDelta) - 1.0F) * (float)PistonBlockTextures.field_31058_d[facing] : (1.0F - getProgress(tickDelta)) * (float)PistonBlockTextures.field_31058_d[facing];
        }

        private void pushEntities(float collisionShapeSizeMultiplier, float entityMoveMultiplier)
        {
            if (!extending)
            {
                --collisionShapeSizeMultiplier;
            }
            else
            {
                collisionShapeSizeMultiplier = 1.0F - collisionShapeSizeMultiplier;
            }

            Box var3 = Block.pistonMoving.getPushedBlockCollisionShape(world, x, y, z, pushedBlockId, collisionShapeSizeMultiplier, facing);
            if (var3 != null)
            {
                var var4 = world.getEntitiesWithinAABBExcludingEntity((Entity)null, var3);
                if (var4.Count > 0)
                {
                    pushedEntities.AddRange(var4);
                    foreach (Entity var6 in pushedEntities)
                    {
                        var6.moveEntity(
                            (double)(entityMoveMultiplier * (float)PistonBlockTextures.field_31056_b[facing]),
                            (double)(entityMoveMultiplier * (float)PistonBlockTextures.field_31059_c[facing]),
                            (double)(entityMoveMultiplier * (float)PistonBlockTextures.field_31058_d[facing])
                        );
                    }
                    pushedEntities.Clear();
                }
            }

        }

        public void finish()
        {
            if (progress < 1.0F)
            {
                progress = lastProgess = 1.0F;
                world.removeBlockTileEntity(x, y, z);
                markRemoved();
                if (world.getBlockId(x, y, z) == Block.pistonMoving.blockID)
                {
                    world.setBlockAndMetadataWithNotify(x, y, z, pushedBlockId, pushedBlockData);
                }
            }

        }

        public override void tick()
        {
            progress = lastProgess;
            if (progress >= 1.0F)
            {
                pushEntities(1.0F, 0.25F);
                world.removeBlockTileEntity(x, y, z);
                markRemoved();
                if (world.getBlockId(x, y, z) == Block.pistonMoving.blockID)
                {
                    world.setBlockAndMetadataWithNotify(x, y, z, pushedBlockId, pushedBlockData);
                }

            }
            else
            {
                lastProgess += 0.5F;
                if (lastProgess >= 1.0F)
                {
                    lastProgess = 1.0F;
                }

                if (extending)
                {
                    pushEntities(lastProgess, lastProgess - progress + 1.0F / 16.0F);
                }

            }
        }

        public override void readNbt(NBTTagCompound nbt)
        {
            base.readNbt(nbt);
            pushedBlockId = nbt.getInteger("blockId");
            pushedBlockData = nbt.getInteger("blockData");
            facing = nbt.getInteger("facing");
            progress = lastProgess = nbt.getFloat("progress");
            extending = nbt.getBoolean("extending");
        }

        public override void writeNbt(NBTTagCompound nbt)
        {
            base.writeNbt(nbt);
            nbt.setInteger("blockId", pushedBlockId);
            nbt.setInteger("blockData", pushedBlockData);
            nbt.setInteger("facing", facing);
            nbt.setFloat("progress", progress);
            nbt.setBoolean("extending", extending);
        }
    }
}