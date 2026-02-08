using betareborn.Blocks;
using betareborn.NBT;
using betareborn.Util.Maths;
using betareborn.Worlds;
using java.lang;

namespace betareborn.Entities
{
    public class EntityFallingSand : Entity
    {
        public static readonly new Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityFallingSand).TypeHandle);
        
        public int blockID;
        public int fallTime = 0;

        public EntityFallingSand(World var1) : base(var1)
        {
        }

        public EntityFallingSand(World var1, double var2, double var4, double var6, int var8) : base(var1)
        {
            blockID = var8;
            preventEntitySpawning = true;
            setSize(0.98F, 0.98F);
            yOffset = height / 2.0F;
            setPosition(var2, var4, var6);
            motionX = 0.0D;
            motionY = 0.0D;
            motionZ = 0.0D;
            prevPosX = var2;
            prevPosY = var4;
            prevPosZ = var6;
        }

        protected override bool canTriggerWalking()
        {
            return false;
        }

        protected override void entityInit()
        {
        }

        public override bool canBeCollidedWith()
        {
            return !isDead;
        }

        public override void onUpdate()
        {
            if (blockID == 0)
            {
                setEntityDead();
            }
            else
            {
                prevPosX = posX;
                prevPosY = posY;
                prevPosZ = posZ;
                ++fallTime;
                motionY -= (double)0.04F;
                moveEntity(motionX, motionY, motionZ);
                motionX *= (double)0.98F;
                motionY *= (double)0.98F;
                motionZ *= (double)0.98F;
                int var1 = MathHelper.floor_double(posX);
                int var2 = MathHelper.floor_double(posY);
                int var3 = MathHelper.floor_double(posZ);
                if (worldObj.getBlockId(var1, var2, var3) == blockID)
                {
                    worldObj.setBlockWithNotify(var1, var2, var3, 0);
                }

                if (onGround)
                {
                    motionX *= (double)0.7F;
                    motionZ *= (double)0.7F;
                    motionY *= -0.5D;
                    setEntityDead();
                    if ((!worldObj.canBlockBePlacedAt(blockID, var1, var2, var3, true, 1) || BlockSand.canFallThrough(worldObj, var1, var2 - 1, var3) || !worldObj.setBlockWithNotify(var1, var2, var3, blockID)) && !worldObj.isRemote)
                    {
                        dropItem(blockID, 1);
                    }
                }
                else if (fallTime > 100 && !worldObj.isRemote)
                {
                    dropItem(blockID, 1);
                    setEntityDead();
                }

            }
        }

        public override void writeEntityToNBT(NBTTagCompound var1)
        {
            var1.setByte("Tile", (sbyte)blockID);
        }

        public override void readEntityFromNBT(NBTTagCompound var1)
        {
            blockID = var1.getByte("Tile") & 255;
        }

        public override float getShadowSize()
        {
            return 0.0F;
        }

        public World getWorld()
        {
            return worldObj;
        }
    }

}