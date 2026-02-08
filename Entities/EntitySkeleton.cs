using betareborn.Items;
using betareborn.NBT;
using betareborn.Util.Maths;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntitySkeleton : EntityMob
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntitySkeleton).TypeHandle);

        private static readonly ItemStack defaultHeldItem = new ItemStack(Item.bow, 1);

        public EntitySkeleton(World var1) : base(var1)
        {
            texture = "/mob/skeleton.png";
        }

        protected override String getLivingSound()
        {
            return "mob.skeleton";
        }

        protected override String getHurtSound()
        {
            return "mob.skeletonhurt";
        }

        protected override String getDeathSound()
        {
            return "mob.skeletonhurt";
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

        protected override void attackEntity(Entity var1, float var2)
        {
            if (var2 < 10.0F)
            {
                double var3 = var1.posX - posX;
                double var5 = var1.posZ - posZ;
                if (attackTime == 0)
                {
                    EntityArrow var7 = new EntityArrow(worldObj, this);
                    var7.posY += (double)1.4F;
                    double var8 = var1.posY + (double)var1.getEyeHeight() - (double)0.2F - var7.posY;
                    float var10 = MathHelper.sqrt_double(var3 * var3 + var5 * var5) * 0.2F;
                    worldObj.playSoundAtEntity(this, "random.bow", 1.0F, 1.0F / (rand.nextFloat() * 0.4F + 0.8F));
                    worldObj.spawnEntity(var7);
                    var7.setArrowHeading(var3, var8 + (double)var10, var5, 0.6F, 12.0F);
                    attackTime = 30;
                }

                rotationYaw = (float)(java.lang.Math.atan2(var5, var3) * 180.0D / (double)((float)Math.PI)) - 90.0F;
                hasAttacked = true;
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
            return Item.arrow.id;
        }

        protected override void dropFewItems()
        {
            int var1 = rand.nextInt(3);

            int var2;
            for (var2 = 0; var2 < var1; ++var2)
            {
                dropItem(Item.arrow.id, 1);
            }

            var1 = rand.nextInt(3);

            for (var2 = 0; var2 < var1; ++var2)
            {
                dropItem(Item.bone.id, 1);
            }

        }

        public override ItemStack getHeldItem()
        {
            return defaultHeldItem;
        }
    }

}