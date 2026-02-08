using betareborn.Items;
using betareborn.Util.Maths;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityZombie : EntityMob
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityZombie).TypeHandle);

        public EntityZombie(World var1) : base(var1)
        {
            texture = "/mob/zombie.png";
            moveSpeed = 0.5F;
            attackStrength = 5;
        }

        public override void onLivingUpdate()
        {
            if (worldObj.isDaytime())
            {
                float var1 = getEntityBrightness(1.0F);
                if (var1 > 0.5F && worldObj.hasSkyLight(MathHelper.floor_double(posX), MathHelper.floor_double(posY), MathHelper.floor_double(posZ)) && rand.nextFloat() * 30.0F < (var1 - 0.4F) * 2.0F)
                {
                    fire = 300;
                }
            }

            base.onLivingUpdate();
        }

        protected override String getLivingSound()
        {
            return "mob.zombie";
        }

        protected override String getHurtSound()
        {
            return "mob.zombiehurt";
        }

        protected override String getDeathSound()
        {
            return "mob.zombiedeath";
        }

        protected override int getDropItemId()
        {
            return Item.feather.id;
        }
    }

}