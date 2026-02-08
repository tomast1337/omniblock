using betareborn.NBT;
using betareborn.Util.Maths;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityMob : EntityCreature, IMob
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityMob).TypeHandle);

        protected int attackStrength = 2;

        public EntityMob(World var1) : base(var1)
        {
            health = 20;
        }

        public override void onLivingUpdate()
        {
            float var1 = getEntityBrightness(1.0F);
            if (var1 > 0.5F)
            {
                entityAge += 2;
            }

            base.onLivingUpdate();
        }

        public override void onUpdate()
        {
            base.onUpdate();
            if (!worldObj.isRemote && worldObj.difficultySetting == 0)
            {
                setEntityDead();
            }

        }

        protected override Entity findPlayerToAttack()
        {
            EntityPlayer var1 = worldObj.getClosestPlayerToEntity(this, 16.0D);
            return var1 != null && canEntityBeSeen(var1) ? var1 : null;
        }

        public override bool attackEntityFrom(Entity var1, int var2)
        {
            if (base.attackEntityFrom(var1, var2))
            {
                if (riddenByEntity != var1 && ridingEntity != var1)
                {
                    if (var1 != this)
                    {
                        playerToAttack = var1;
                    }

                    return true;
                }
                else
                {
                    return true;
                }
            }
            else
            {
                return false;
            }
        }

        protected override void attackEntity(Entity var1, float var2)
        {
            if (attackTime <= 0 && var2 < 2.0F && var1.boundingBox.maxY > boundingBox.minY && var1.boundingBox.minY < boundingBox.maxY)
            {
                attackTime = 20;
                var1.attackEntityFrom(this, attackStrength);
            }

        }

        protected override float getBlockPathWeight(int var1, int var2, int var3)
        {
            return 0.5F - worldObj.getLuminance(var1, var2, var3);
        }

        public override void writeEntityToNBT(NBTTagCompound var1)
        {
            base.writeEntityToNBT(var1);
        }

        public override void readEntityFromNBT(NBTTagCompound var1)
        {
            base.readEntityFromNBT(var1);
        }

        public override bool canSpawn()
        {
            int var1 = MathHelper.floor_double(posX);
            int var2 = MathHelper.floor_double(boundingBox.minY);
            int var3 = MathHelper.floor_double(posZ);
            if (worldObj.getBrightness(LightType.Sky, var1, var2, var3) > rand.nextInt(32))
            {
                return false;
            }
            else
            {
                int var4 = worldObj.getLightLevel(var1, var2, var3);
                if (worldObj.func_27160_B())
                {
                    int var5 = worldObj.skylightSubtracted;
                    worldObj.skylightSubtracted = 10;
                    var4 = worldObj.getLightLevel(var1, var2, var3);
                    worldObj.skylightSubtracted = var5;
                }

                return var4 <= rand.nextInt(8) && base.canSpawn();
            }
        }
    }

}