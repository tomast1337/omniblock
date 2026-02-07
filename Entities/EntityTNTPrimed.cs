using betareborn.NBT;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityTNTPrimed : Entity
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityTNTPrimed).TypeHandle);
        public int fuse;

        public EntityTNTPrimed(World var1) : base(var1)
        {
            fuse = 0;
            preventEntitySpawning = true;
            setSize(0.98F, 0.98F);
            yOffset = height / 2.0F;
        }

        public EntityTNTPrimed(World var1, double var2, double var4, double var6) : base(var1)
        {
            setPosition(var2, var4, var6);
            float var8 = (float)(java.lang.Math.random() * (double)((float)Math.PI) * 2.0D);
            motionX = (double)(-MathHelper.sin(var8 * (float)Math.PI / 180.0F) * 0.02F);
            motionY = (double)0.2F;
            motionZ = (double)(-MathHelper.cos(var8 * (float)Math.PI / 180.0F) * 0.02F);
            fuse = 80;
            prevPosX = var2;
            prevPosY = var4;
            prevPosZ = var6;
        }

        protected override void entityInit()
        {
        }

        protected override bool canTriggerWalking()
        {
            return false;
        }

        public override bool canBeCollidedWith()
        {
            return !isDead;
        }

        public override void onUpdate()
        {
            prevPosX = posX;
            prevPosY = posY;
            prevPosZ = posZ;
            motionY -= (double)0.04F;
            moveEntity(motionX, motionY, motionZ);
            motionX *= (double)0.98F;
            motionY *= (double)0.98F;
            motionZ *= (double)0.98F;
            if (onGround)
            {
                motionX *= (double)0.7F;
                motionZ *= (double)0.7F;
                motionY *= -0.5D;
            }

            if (fuse-- <= 0)
            {
                if (!worldObj.multiplayerWorld)
                {
                    setEntityDead();
                    explode();
                }
                else
                {
                    setEntityDead();
                }
            }
            else
            {
                worldObj.addParticle("smoke", posX, posY + 0.5D, posZ, 0.0D, 0.0D, 0.0D);
            }

        }

        private void explode()
        {
            float var1 = 4.0F;
            worldObj.createExplosion((Entity)null, posX, posY, posZ, var1);
        }

        public override void writeEntityToNBT(NBTTagCompound var1)
        {
            var1.setByte("Fuse", (sbyte)fuse);
        }

        public override void readEntityFromNBT(NBTTagCompound var1)
        {
            fuse = var1.getByte("Fuse");
        }

        public override float getShadowSize()
        {
            return 0.0F;
        }
    }

}