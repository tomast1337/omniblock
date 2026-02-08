using betareborn.Entities;
using betareborn.Items;
using betareborn.Util.Maths;
using betareborn.Worlds;

namespace betareborn.Client.Network
{
    public class OtherPlayerEntity : EntityPlayer
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(OtherPlayerEntity).TypeHandle);

        private int lerpSteps;
        private double lerpX;
        private double lerpY;
        private double lerpZ;
        private double lerpYaw;
        private double lerpPitch;

        public OtherPlayerEntity(World var1, string var2) : base(var1)
        {
            username = var2;
            yOffset = 0.0F;
            stepHeight = 0.0F;
            if (var2 != null && var2.Length > 0)
            {
                skinUrl = "http://s3.amazonaws.com/MinecraftSkins/" + var2 + ".png";
            }

            noClip = true;
            sleepOffsetY = 0.25F;
            renderDistanceWeight = 10.0D;
        }

        protected override void resetEyeHeight()
        {
            yOffset = 0.0F;
        }

        public override bool damage(Entity var1, int var2)
        {
            return true;
        }

        public override void setPositionAndRotation2(double var1, double var3, double var5, float var7, float var8, int var9)
        {
            lerpX = var1;
            lerpY = var3;
            lerpZ = var5;
            lerpYaw = var7;
            lerpPitch = var8;
            lerpSteps = var9;
        }

        public override void onUpdate()
        {
            sleepOffsetY = 0.0F;
            base.onUpdate();
            lastWalkAnimationSpeed = walkAnimationSpeed;
            double var1 = posX - prevPosX;
            double var3 = posZ - prevPosZ;
            float var5 = MathHelper.sqrt_double(var1 * var1 + var3 * var3) * 4.0F;
            if (var5 > 1.0F)
            {
                var5 = 1.0F;
            }

            walkAnimationSpeed += (var5 - walkAnimationSpeed) * 0.4F;
            field_703_S += walkAnimationSpeed;
        }

        public override float getShadowRadius()
        {
            return 0.0F;
        }

        public override void tickMovement()
        {
            base.tickLiving();
            if (lerpSteps > 0)
            {
                double var1 = posX + (lerpX - posX) / lerpSteps;
                double var3 = posY + (lerpY - posY) / lerpSteps;
                double var5 = posZ + (lerpZ - posZ) / lerpSteps;

                double var7;
                for (var7 = lerpYaw - rotationYaw; var7 < -180.0D; var7 += 360.0D)
                {
                }

                while (var7 >= 180.0D)
                {
                    var7 -= 360.0D;
                }

                rotationYaw = (float)(rotationYaw + var7 / lerpSteps);
                rotationPitch = (float)(rotationPitch + (lerpPitch - rotationPitch) / lerpSteps);
                --lerpSteps;
                setPosition(var1, var3, var5);
                setRotation(rotationYaw, rotationPitch);
            }

            prevStepBobbingAmount = stepBobbingAmount;
            float var9 = MathHelper.sqrt_double(motionX * motionX + motionZ * motionZ);
            float var2 = (float)java.lang.Math.atan(-motionY * (double)0.2F) * 15.0F;
            if (var9 > 0.1F)
            {
                var9 = 0.1F;
            }

            if (!onGround || health <= 0)
            {
                var9 = 0.0F;
            }

            if (onGround || health <= 0)
            {
                var2 = 0.0F;
            }

            stepBobbingAmount += (var9 - stepBobbingAmount) * 0.4F;
            tilt += (var2 - tilt) * 0.8F;
        }

        public override void setEquipmentStack(int var1, int var2, int var3)
        {
            ItemStack var4 = null;
            if (var2 >= 0)
            {
                var4 = new ItemStack(var2, 1, var3);
            }

            if (var1 == 0)
            {
                inventory.mainInventory[inventory.currentItem] = var4;
            }
            else
            {
                inventory.armorInventory[var1 - 1] = var4;
            }

        }

        public override void spawn()
        {
        }
    }

}