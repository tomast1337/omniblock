using betareborn.Items;
using betareborn.NBT;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntitySnowball : Entity
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntitySnowball).TypeHandle);

        private int xTileSnowball = -1;
        private int yTileSnowball = -1;
        private int zTileSnowball = -1;
        private int inTileSnowball = 0;
        private bool inGroundSnowball = false;
        public int shakeSnowball = 0;
        private EntityLiving thrower;
        private int ticksInGroundSnowball;
        private int ticksInAirSnowball = 0;

        public EntitySnowball(World var1) : base(var1)
        {
            setSize(0.25F, 0.25F);
        }

        protected override void entityInit()
        {
        }

        public override bool isInRangeToRenderDist(double var1)
        {
            double var3 = boundingBox.getAverageSizeLength() * 4.0D;
            var3 *= 64.0D;
            return var1 < var3 * var3;
        }

        public EntitySnowball(World var1, EntityLiving var2) : base(var1)
        {
            thrower = var2;
            setSize(0.25F, 0.25F);
            setLocationAndAngles(var2.posX, var2.posY + (double)var2.getEyeHeight(), var2.posZ, var2.rotationYaw, var2.rotationPitch);
            posX -= (double)(MathHelper.cos(rotationYaw / 180.0F * (float)java.lang.Math.PI) * 0.16F);
            posY -= (double)0.1F;
            posZ -= (double)(MathHelper.sin(rotationYaw / 180.0F * (float)java.lang.Math.PI) * 0.16F);
            setPosition(posX, posY, posZ);
            yOffset = 0.0F;
            float var3 = 0.4F;
            motionX = (double)(-MathHelper.sin(rotationYaw / 180.0F * (float)java.lang.Math.PI) * MathHelper.cos(rotationPitch / 180.0F * (float)java.lang.Math.PI) * var3);
            motionZ = (double)(MathHelper.cos(rotationYaw / 180.0F * (float)java.lang.Math.PI) * MathHelper.cos(rotationPitch / 180.0F * (float)java.lang.Math.PI) * var3);
            motionY = (double)(-MathHelper.sin(rotationPitch / 180.0F * (float)java.lang.Math.PI) * var3);
            setSnowballHeading(motionX, motionY, motionZ, 1.5F, 1.0F);
        }

        public EntitySnowball(World var1, double var2, double var4, double var6) : base(var1)
        {
            ticksInGroundSnowball = 0;
            setSize(0.25F, 0.25F);
            setPosition(var2, var4, var6);
            yOffset = 0.0F;
        }

        public void setSnowballHeading(double var1, double var3, double var5, float var7, float var8)
        {
            float var9 = MathHelper.sqrt_double(var1 * var1 + var3 * var3 + var5 * var5);
            var1 /= (double)var9;
            var3 /= (double)var9;
            var5 /= (double)var9;
            var1 += rand.nextGaussian() * (double)0.0075F * (double)var8;
            var3 += rand.nextGaussian() * (double)0.0075F * (double)var8;
            var5 += rand.nextGaussian() * (double)0.0075F * (double)var8;
            var1 *= (double)var7;
            var3 *= (double)var7;
            var5 *= (double)var7;
            motionX = var1;
            motionY = var3;
            motionZ = var5;
            float var10 = MathHelper.sqrt_double(var1 * var1 + var5 * var5);
            prevRotationYaw = rotationYaw = (float)(java.lang.Math.atan2(var1, var5) * 180.0D / (double)((float)java.lang.Math.PI));
            prevRotationPitch = rotationPitch = (float)(java.lang.Math.atan2(var3, (double)var10) * 180.0D / (double)((float)java.lang.Math.PI));
            ticksInGroundSnowball = 0;
        }

        public override void setVelocity(double var1, double var3, double var5)
        {
            motionX = var1;
            motionY = var3;
            motionZ = var5;
            if (prevRotationPitch == 0.0F && prevRotationYaw == 0.0F)
            {
                float var7 = MathHelper.sqrt_double(var1 * var1 + var5 * var5);
                prevRotationYaw = rotationYaw = (float)(java.lang.Math.atan2(var1, var5) * 180.0D / (double)((float)java.lang.Math.PI));
                prevRotationPitch = rotationPitch = (float)(java.lang.Math.atan2(var3, (double)var7) * 180.0D / (double)((float)java.lang.Math.PI));
            }

        }

        public override void onUpdate()
        {
            lastTickPosX = posX;
            lastTickPosY = posY;
            lastTickPosZ = posZ;
            base.onUpdate();
            if (shakeSnowball > 0)
            {
                --shakeSnowball;
            }

            if (inGroundSnowball)
            {
                int var1 = worldObj.getBlockId(xTileSnowball, yTileSnowball, zTileSnowball);
                if (var1 == inTileSnowball)
                {
                    ++ticksInGroundSnowball;
                    if (ticksInGroundSnowball == 1200)
                    {
                        setEntityDead();
                    }

                    return;
                }

                inGroundSnowball = false;
                motionX *= (double)(rand.nextFloat() * 0.2F);
                motionY *= (double)(rand.nextFloat() * 0.2F);
                motionZ *= (double)(rand.nextFloat() * 0.2F);
                ticksInGroundSnowball = 0;
                ticksInAirSnowball = 0;
            }
            else
            {
                ++ticksInAirSnowball;
            }

            Vec3D var15 = Vec3D.createVector(posX, posY, posZ);
            Vec3D var2 = Vec3D.createVector(posX + motionX, posY + motionY, posZ + motionZ);
            MovingObjectPosition var3 = worldObj.rayTraceBlocks(var15, var2);
            var15 = Vec3D.createVector(posX, posY, posZ);
            var2 = Vec3D.createVector(posX + motionX, posY + motionY, posZ + motionZ);
            if (var3 != null)
            {
                var2 = Vec3D.createVector(var3.hitVec.xCoord, var3.hitVec.yCoord, var3.hitVec.zCoord);
            }

            if (!worldObj.multiplayerWorld)
            {
                Entity var4 = null;
                var var5 = worldObj.getEntitiesWithinAABBExcludingEntity(this, boundingBox.stretch(motionX, motionY, motionZ).expand(1.0D, 1.0D, 1.0D));
                double var6 = 0.0D;

                for (int var8 = 0; var8 < var5.Count; ++var8)
                {
                    Entity var9 = var5[var8];
                    if (var9.canBeCollidedWith() && (var9 != thrower || ticksInAirSnowball >= 5))
                    {
                        float var10 = 0.3F;
                        Box var11 = var9.boundingBox.expand((double)var10, (double)var10, (double)var10);
                        MovingObjectPosition var12 = var11.raycast(var15, var2);
                        if (var12 != null)
                        {
                            double var13 = var15.distanceTo(var12.hitVec);
                            if (var13 < var6 || var6 == 0.0D)
                            {
                                var4 = var9;
                                var6 = var13;
                            }
                        }
                    }
                }

                if (var4 != null)
                {
                    var3 = new MovingObjectPosition(var4);
                }
            }

            if (var3 != null)
            {
                if (var3.entityHit != null && var3.entityHit.attackEntityFrom(thrower, 0))
                {
                }

                for (int var16 = 0; var16 < 8; ++var16)
                {
                    worldObj.addParticle("snowballpoof", posX, posY, posZ, 0.0D, 0.0D, 0.0D);
                }

                setEntityDead();
            }

            posX += motionX;
            posY += motionY;
            posZ += motionZ;
            float var17 = MathHelper.sqrt_double(motionX * motionX + motionZ * motionZ);
            rotationYaw = (float)(java.lang.Math.atan2(motionX, motionZ) * 180.0D / (double)((float)java.lang.Math.PI));

            for (rotationPitch = (float)(java.lang.Math.atan2(motionY, (double)var17) * 180.0D / (double)((float)java.lang.Math.PI)); rotationPitch - prevRotationPitch < -180.0F; prevRotationPitch -= 360.0F)
            {
            }

            while (rotationPitch - prevRotationPitch >= 180.0F)
            {
                prevRotationPitch += 360.0F;
            }

            while (rotationYaw - prevRotationYaw < -180.0F)
            {
                prevRotationYaw -= 360.0F;
            }

            while (rotationYaw - prevRotationYaw >= 180.0F)
            {
                prevRotationYaw += 360.0F;
            }

            rotationPitch = prevRotationPitch + (rotationPitch - prevRotationPitch) * 0.2F;
            rotationYaw = prevRotationYaw + (rotationYaw - prevRotationYaw) * 0.2F;
            float var18 = 0.99F;
            float var19 = 0.03F;
            if (isInWater())
            {
                for (int var7 = 0; var7 < 4; ++var7)
                {
                    float var20 = 0.25F;
                    worldObj.addParticle("bubble", posX - motionX * (double)var20, posY - motionY * (double)var20, posZ - motionZ * (double)var20, motionX, motionY, motionZ);
                }

                var18 = 0.8F;
            }

            motionX *= (double)var18;
            motionY *= (double)var18;
            motionZ *= (double)var18;
            motionY -= (double)var19;
            setPosition(posX, posY, posZ);
        }

        public override void writeEntityToNBT(NBTTagCompound var1)
        {
            var1.setShort("xTile", (short)xTileSnowball);
            var1.setShort("yTile", (short)yTileSnowball);
            var1.setShort("zTile", (short)zTileSnowball);
            var1.setByte("inTile", (sbyte)inTileSnowball);
            var1.setByte("shake", (sbyte)shakeSnowball);
            var1.setByte("inGround", (sbyte)(inGroundSnowball ? 1 : 0));
        }

        public override void readEntityFromNBT(NBTTagCompound var1)
        {
            xTileSnowball = var1.getShort("xTile");
            yTileSnowball = var1.getShort("yTile");
            zTileSnowball = var1.getShort("zTile");
            inTileSnowball = var1.getByte("inTile") & 255;
            shakeSnowball = var1.getByte("shake") & 255;
            inGroundSnowball = var1.getByte("inGround") == 1;
        }

        public override void onCollideWithPlayer(EntityPlayer var1)
        {
            if (inGroundSnowball && thrower == var1 && shakeSnowball <= 0 && var1.inventory.addItemStackToInventory(new ItemStack(Item.arrow, 1)))
            {
                worldObj.playSoundAtEntity(this, "random.pop", 0.2F, ((rand.nextFloat() - rand.nextFloat()) * 0.7F + 1.0F) * 2.0F);
                var1.onItemPickup(this, 1);
                setEntityDead();
            }

        }

        public override float getShadowSize()
        {
            return 0.0F;
        }
    }

}