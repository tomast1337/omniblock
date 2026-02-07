using betareborn.NBT;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityFireball : Entity
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityFireball).TypeHandle);

        private int field_9402_e = -1;
        private int field_9401_f = -1;
        private int field_9400_g = -1;
        private int field_9399_h = 0;
        private bool field_9398_i = false;
        public int field_9406_a = 0;
        public EntityLiving field_9397_j;
        private int field_9396_k;
        private int field_9395_l = 0;
        public double field_9405_b;
        public double field_9404_c;
        public double field_9403_d;

        public EntityFireball(World var1) : base(var1)
        {
            setSize(1.0F, 1.0F);
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

        public EntityFireball(World var1, double var2, double var4, double var6, double var8, double var10, double var12) : base(var1)
        {
            setSize(1.0F, 1.0F);
            setLocationAndAngles(var2, var4, var6, rotationYaw, rotationPitch);
            setPosition(var2, var4, var6);
            double var14 = (double)MathHelper.sqrt_double(var8 * var8 + var10 * var10 + var12 * var12);
            field_9405_b = var8 / var14 * 0.1D;
            field_9404_c = var10 / var14 * 0.1D;
            field_9403_d = var12 / var14 * 0.1D;
        }

        public EntityFireball(World var1, EntityLiving var2, double var3, double var5, double var7) : base(var1)
        {
            field_9397_j = var2;
            setSize(1.0F, 1.0F);
            setLocationAndAngles(var2.posX, var2.posY, var2.posZ, var2.rotationYaw, var2.rotationPitch);
            setPosition(posX, posY, posZ);
            yOffset = 0.0F;
            motionX = motionY = motionZ = 0.0D;
            var3 += rand.nextGaussian() * 0.4D;
            var5 += rand.nextGaussian() * 0.4D;
            var7 += rand.nextGaussian() * 0.4D;
            double var9 = (double)MathHelper.sqrt_double(var3 * var3 + var5 * var5 + var7 * var7);
            field_9405_b = var3 / var9 * 0.1D;
            field_9404_c = var5 / var9 * 0.1D;
            field_9403_d = var7 / var9 * 0.1D;
        }

        public override void onUpdate()
        {
            base.onUpdate();
            fire = 10;
            if (field_9406_a > 0)
            {
                --field_9406_a;
            }

            if (field_9398_i)
            {
                int var1 = worldObj.getBlockId(field_9402_e, field_9401_f, field_9400_g);
                if (var1 == field_9399_h)
                {
                    ++field_9396_k;
                    if (field_9396_k == 1200)
                    {
                        setEntityDead();
                    }

                    return;
                }

                field_9398_i = false;
                motionX *= (double)(rand.nextFloat() * 0.2F);
                motionY *= (double)(rand.nextFloat() * 0.2F);
                motionZ *= (double)(rand.nextFloat() * 0.2F);
                field_9396_k = 0;
                field_9395_l = 0;
            }
            else
            {
                ++field_9395_l;
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

            Entity var4 = null;
            var var5 = worldObj.getEntitiesWithinAABBExcludingEntity(this, boundingBox.stretch(motionX, motionY, motionZ).expand(1.0D, 1.0D, 1.0D));
            double var6 = 0.0D;

            for (int var8 = 0; var8 < var5.Count; ++var8)
            {
                Entity var9 = var5[var8];
                if (var9.canBeCollidedWith() && (var9 != field_9397_j || field_9395_l >= 25))
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

            if (var3 != null)
            {
                if (!worldObj.multiplayerWorld)
                {
                    if (var3.entityHit != null && var3.entityHit.attackEntityFrom(field_9397_j, 0))
                    {
                    }

                    worldObj.newExplosion((Entity)null, posX, posY, posZ, 1.0F, true);
                }

                setEntityDead();
            }

            posX += motionX;
            posY += motionY;
            posZ += motionZ;
            float var16 = MathHelper.sqrt_double(motionX * motionX + motionZ * motionZ);
            rotationYaw = (float)(java.lang.Math.atan2(motionX, motionZ) * 180.0D / (double)((float)Math.PI));

            for (rotationPitch = (float)(java.lang.Math.atan2(motionY, (double)var16) * 180.0D / (double)((float)Math.PI)); rotationPitch - prevRotationPitch < -180.0F; prevRotationPitch -= 360.0F)
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
            float var17 = 0.95F;
            if (isInWater())
            {
                for (int var18 = 0; var18 < 4; ++var18)
                {
                    float var19 = 0.25F;
                    worldObj.addParticle("bubble", posX - motionX * (double)var19, posY - motionY * (double)var19, posZ - motionZ * (double)var19, motionX, motionY, motionZ);
                }

                var17 = 0.8F;
            }

            motionX += field_9405_b;
            motionY += field_9404_c;
            motionZ += field_9403_d;
            motionX *= (double)var17;
            motionY *= (double)var17;
            motionZ *= (double)var17;
            worldObj.addParticle("smoke", posX, posY + 0.5D, posZ, 0.0D, 0.0D, 0.0D);
            setPosition(posX, posY, posZ);
        }

        public override void writeEntityToNBT(NBTTagCompound var1)
        {
            var1.setShort("xTile", (short)field_9402_e);
            var1.setShort("yTile", (short)field_9401_f);
            var1.setShort("zTile", (short)field_9400_g);
            var1.setByte("inTile", (sbyte)field_9399_h);
            var1.setByte("shake", (sbyte)field_9406_a);
            var1.setByte("inGround", (sbyte)(field_9398_i ? 1 : 0));
        }

        public override void readEntityFromNBT(NBTTagCompound var1)
        {
            field_9402_e = var1.getShort("xTile");
            field_9401_f = var1.getShort("yTile");
            field_9400_g = var1.getShort("zTile");
            field_9399_h = var1.getByte("inTile") & 255;
            field_9406_a = var1.getByte("shake") & 255;
            field_9398_i = var1.getByte("inGround") == 1;
        }

        public override bool canBeCollidedWith()
        {
            return true;
        }

        public override float getCollisionBorderSize()
        {
            return 1.0F;
        }

        public override bool attackEntityFrom(Entity var1, int var2)
        {
            setBeenAttacked();
            if (var1 != null)
            {
                Vec3D var3 = var1.getLookVec();
                if (var3 != null)
                {
                    motionX = var3.xCoord;
                    motionY = var3.yCoord;
                    motionZ = var3.zCoord;
                    field_9405_b = motionX * 0.1D;
                    field_9404_c = motionY * 0.1D;
                    field_9403_d = motionZ * 0.1D;
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        public override float getShadowSize()
        {
            return 0.0F;
        }
    }

}