using betareborn.Items;
using betareborn.NBT;
using betareborn.Util.Maths;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntitySpider : EntityMob
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntitySpider).TypeHandle);

        public EntitySpider(World var1) : base(var1)
        {
            texture = "/mob/spider.png";
            setSize(1.4F, 0.9F);
            moveSpeed = 0.8F;
        }

        public override double getMountedYOffset()
        {
            return (double)height * 0.75D - 0.5D;
        }

        protected override bool canTriggerWalking()
        {
            return false;
        }

        protected override Entity findPlayerToAttack()
        {
            float var1 = getEntityBrightness(1.0F);
            if (var1 < 0.5F)
            {
                double var2 = 16.0D;
                return worldObj.getClosestPlayerToEntity(this, var2);
            }
            else
            {
                return null;
            }
        }

        protected override string getLivingSound()
        {
            return "mob.spider";
        }

        protected override string getHurtSound()
        {
            return "mob.spider";
        }

        protected override string getDeathSound()
        {
            return "mob.spiderdeath";
        }

        protected override void attackEntity(Entity var1, float var2)
        {
            float var3 = getEntityBrightness(1.0F);
            if (var3 > 0.5F && rand.nextInt(100) == 0)
            {
                playerToAttack = null;
            }
            else
            {
                if (var2 > 2.0F && var2 < 6.0F && rand.nextInt(10) == 0)
                {
                    if (onGround)
                    {
                        double var4 = var1.posX - posX;
                        double var6 = var1.posZ - posZ;
                        float var8 = MathHelper.sqrt_double(var4 * var4 + var6 * var6);
                        motionX = var4 / (double)var8 * 0.5D * (double)0.8F + motionX * (double)0.2F;
                        motionZ = var6 / (double)var8 * 0.5D * (double)0.8F + motionZ * (double)0.2F;
                        motionY = (double)0.4F;
                    }
                }
                else
                {
                    base.attackEntity(var1, var2);
                }

            }
        }

        public override void writeEntityToNBT(NBTTagCompound var1)
        {
            base.writeEntityToNBT(var1);
        }

        public override void readEntityFromNBT(NBTTagCompound var1)
        {
            base.readEntityFromNBT(var1);
        }

        protected override int getDropItemId()
        {
            return Item.silk.id;
        }

        public override bool isOnLadder()
        {
            return isCollidedHorizontally;
        }
    }

}