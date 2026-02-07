using betareborn.Items;
using betareborn.NBT;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityEgg : Entity
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityEgg).TypeHandle);

        private int field_20056_b = -1;
        private int field_20055_c = -1;
        private int field_20054_d = -1;
        private int field_20053_e = 0;
        private bool field_20052_f = false;
        public int field_20057_a = 0;
        private EntityLiving field_20051_g;
        private int field_20050_h;
        private int field_20049_i = 0;

        public EntityEgg(World var1) : base(var1)
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

        public EntityEgg(World var1, EntityLiving var2) : base(var1)
        {
            field_20051_g = var2;
            setSize(0.25F, 0.25F);
            setLocationAndAngles(var2.posX, var2.posY + (double)var2.getEyeHeight(), var2.posZ, var2.rotationYaw, var2.rotationPitch);
            posX -= (double)(MathHelper.cos(rotationYaw / 180.0F * (float)Math.PI) * 0.16F);
            posY -= (double)0.1F;
            posZ -= (double)(MathHelper.sin(rotationYaw / 180.0F * (float)Math.PI) * 0.16F);
            setPosition(posX, posY, posZ);
            yOffset = 0.0F;
            float var3 = 0.4F;
            motionX = (double)(-MathHelper.sin(rotationYaw / 180.0F * (float)Math.PI) * MathHelper.cos(rotationPitch / 180.0F * (float)Math.PI) * var3);
            motionZ = (double)(MathHelper.cos(rotationYaw / 180.0F * (float)Math.PI) * MathHelper.cos(rotationPitch / 180.0F * (float)Math.PI) * var3);
            motionY = (double)(-MathHelper.sin(rotationPitch / 180.0F * (float)Math.PI) * var3);
            setEggHeading(motionX, motionY, motionZ, 1.5F, 1.0F);
        }

        public EntityEgg(World var1, double var2, double var4, double var6) : base(var1)
        {
            field_20050_h = 0;
            setSize(0.25F, 0.25F);
            setPosition(var2, var4, var6);
            yOffset = 0.0F;
        }

        public void setEggHeading(double var1, double var3, double var5, float var7, float var8)
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
            prevRotationYaw = rotationYaw = (float)(java.lang.Math.atan2(var1, var5) * 180.0D / (double)((float)Math.PI));
            prevRotationPitch = rotationPitch = (float)(java.lang.Math.atan2(var3, (double)var10) * 180.0D / (double)((float)Math.PI));
            field_20050_h = 0;
        }

        public override void setVelocity(double var1, double var3, double var5)
        {
            motionX = var1;
            motionY = var3;
            motionZ = var5;
            if (prevRotationPitch == 0.0F && prevRotationYaw == 0.0F)
            {
                float var7 = MathHelper.sqrt_double(var1 * var1 + var5 * var5);
                prevRotationYaw = rotationYaw = (float)(java.lang.Math.atan2(var1, var5) * 180.0D / (double)((float)Math.PI));
                prevRotationPitch = rotationPitch = (float)(java.lang.Math.atan2(var3, (double)var7) * 180.0D / (double)((float)Math.PI));
            }

        }

        public override void onUpdate()
        {
            lastTickPosX = posX;
            lastTickPosY = posY;
            lastTickPosZ = posZ;
            base.onUpdate();
            if (field_20057_a > 0)
            {
                --field_20057_a;
            }

            if (field_20052_f)
            {
                int var1 = worldObj.getBlockId(field_20056_b, field_20055_c, field_20054_d);
                if (var1 == field_20053_e)
                {
                    ++field_20050_h;
                    if (field_20050_h == 1200)
                    {
                        setEntityDead();
                    }

                    return;
                }

                field_20052_f = false;
                motionX *= (double)(rand.nextFloat() * 0.2F);
                motionY *= (double)(rand.nextFloat() * 0.2F);
                motionZ *= (double)(rand.nextFloat() * 0.2F);
                field_20050_h = 0;
                field_20049_i = 0;
            }
            else
            {
                ++field_20049_i;
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
                    if (var9.canBeCollidedWith() && (var9 != field_20051_g || field_20049_i >= 5))
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
                if (var3.entityHit != null && var3.entityHit.attackEntityFrom(field_20051_g, 0))
                {
                }

                if (!worldObj.multiplayerWorld && rand.nextInt(8) == 0)
                {
                    byte var16 = 1;
                    if (rand.nextInt(32) == 0)
                    {
                        var16 = 4;
                    }

                    for (int var17 = 0; var17 < var16; ++var17)
                    {
                        EntityChicken var21 = new EntityChicken(worldObj);
                        var21.setLocationAndAngles(posX, posY, posZ, rotationYaw, 0.0F);
                        worldObj.entityJoinedWorld(var21);
                    }
                }

                for (int var18 = 0; var18 < 8; ++var18)
                {
                    worldObj.addParticle("snowballpoof", posX, posY, posZ, 0.0D, 0.0D, 0.0D);
                }

                setEntityDead();
            }

            posX += motionX;
            posY += motionY;
            posZ += motionZ;
            float var20 = MathHelper.sqrt_double(motionX * motionX + motionZ * motionZ);
            rotationYaw = (float)(java.lang.Math.atan2(motionX, motionZ) * 180.0D / (double)((float)Math.PI));

            for (rotationPitch = (float)(java.lang.Math.atan2(motionY, (double)var20) * 180.0D / (double)((float)Math.PI)); rotationPitch - prevRotationPitch < -180.0F; prevRotationPitch -= 360.0F)
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
            float var19 = 0.99F;
            float var22 = 0.03F;
            if (isInWater())
            {
                for (int var7 = 0; var7 < 4; ++var7)
                {
                    float var23 = 0.25F;
                    worldObj.addParticle("bubble", posX - motionX * (double)var23, posY - motionY * (double)var23, posZ - motionZ * (double)var23, motionX, motionY, motionZ);
                }

                var19 = 0.8F;
            }

            motionX *= (double)var19;
            motionY *= (double)var19;
            motionZ *= (double)var19;
            motionY -= (double)var22;
            setPosition(posX, posY, posZ);
        }

        public override void writeEntityToNBT(NBTTagCompound var1)
        {
            var1.setShort("xTile", (short)field_20056_b);
            var1.setShort("yTile", (short)field_20055_c);
            var1.setShort("zTile", (short)field_20054_d);
            var1.setByte("inTile", (sbyte)field_20053_e);
            var1.setByte("shake", (sbyte)field_20057_a);
            var1.setByte("inGround", (sbyte)(field_20052_f ? 1 : 0));
        }

        public override void readEntityFromNBT(NBTTagCompound var1)
        {
            field_20056_b = var1.getShort("xTile");
            field_20055_c = var1.getShort("yTile");
            field_20054_d = var1.getShort("zTile");
            field_20053_e = var1.getByte("inTile") & 255;
            field_20057_a = var1.getByte("shake") & 255;
            field_20052_f = var1.getByte("inGround") == 1;
        }

        public override void onCollideWithPlayer(EntityPlayer var1)
        {
            if (field_20052_f && field_20051_g == var1 && field_20057_a <= 0 && var1.inventory.addItemStackToInventory(new ItemStack(Item.arrow, 1)))
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