using betareborn.NBT;
using betareborn.Util.Hit;
using betareborn.Util.Maths;
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
            setBoundingBoxSpacing(1.0F, 1.0F);
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
            setBoundingBoxSpacing(1.0F, 1.0F);
            setPositionAndAnglesKeepPrevAngles(var2, var4, var6, yaw, pitch);
            setPosition(var2, var4, var6);
            double var14 = (double)MathHelper.sqrt_double(var8 * var8 + var10 * var10 + var12 * var12);
            field_9405_b = var8 / var14 * 0.1D;
            field_9404_c = var10 / var14 * 0.1D;
            field_9403_d = var12 / var14 * 0.1D;
        }

        public EntityFireball(World var1, EntityLiving var2, double var3, double var5, double var7) : base(var1)
        {
            field_9397_j = var2;
            setBoundingBoxSpacing(1.0F, 1.0F);
            setPositionAndAnglesKeepPrevAngles(var2.x, var2.y, var2.z, var2.yaw, var2.pitch);
            setPosition(x, y, z);
            standingEyeHeight = 0.0F;
            velocityX = velocityY = velocityZ = 0.0D;
            var3 += random.nextGaussian() * 0.4D;
            var5 += random.nextGaussian() * 0.4D;
            var7 += random.nextGaussian() * 0.4D;
            double var9 = (double)MathHelper.sqrt_double(var3 * var3 + var5 * var5 + var7 * var7);
            field_9405_b = var3 / var9 * 0.1D;
            field_9404_c = var5 / var9 * 0.1D;
            field_9403_d = var7 / var9 * 0.1D;
        }

        public override void onUpdate()
        {
            base.onUpdate();
            fireTicks = 10;
            if (field_9406_a > 0)
            {
                --field_9406_a;
            }

            if (field_9398_i)
            {
                int var1 = world.getBlockId(field_9402_e, field_9401_f, field_9400_g);
                if (var1 == field_9399_h)
                {
                    ++field_9396_k;
                    if (field_9396_k == 1200)
                    {
                        markDead();
                    }

                    return;
                }

                field_9398_i = false;
                velocityX *= (double)(random.nextFloat() * 0.2F);
                velocityY *= (double)(random.nextFloat() * 0.2F);
                velocityZ *= (double)(random.nextFloat() * 0.2F);
                field_9396_k = 0;
                field_9395_l = 0;
            }
            else
            {
                ++field_9395_l;
            }

            Vec3D var15 = Vec3D.createVector(x, y, z);
            Vec3D var2 = Vec3D.createVector(x + velocityX, y + velocityY, z + velocityZ);
            HitResult var3 = world.raycast(var15, var2);
            var15 = Vec3D.createVector(x, y, z);
            var2 = Vec3D.createVector(x + velocityX, y + velocityY, z + velocityZ);
            if (var3 != null)
            {
                var2 = Vec3D.createVector(var3.pos.xCoord, var3.pos.yCoord, var3.pos.zCoord);
            }

            Entity var4 = null;
            var var5 = world.getEntities(this, boundingBox.stretch(velocityX, velocityY, velocityZ).expand(1.0D, 1.0D, 1.0D));
            double var6 = 0.0D;

            for (int var8 = 0; var8 < var5.Count; ++var8)
            {
                Entity var9 = var5[var8];
                if (var9.canBeCollidedWith() && (var9 != field_9397_j || field_9395_l >= 25))
                {
                    float var10 = 0.3F;
                    Box var11 = var9.boundingBox.expand((double)var10, (double)var10, (double)var10);
                    HitResult var12 = var11.raycast(var15, var2);
                    if (var12 != null)
                    {
                        double var13 = var15.distanceTo(var12.pos);
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
                var3 = new HitResult(var4);
            }

            if (var3 != null)
            {
                if (!world.isRemote)
                {
                    if (var3.entity != null && var3.entity.damage(field_9397_j, 0))
                    {
                    }

                    world.newExplosion((Entity)null, x, y, z, 1.0F, true);
                }

                markDead();
            }

            x += velocityX;
            y += velocityY;
            z += velocityZ;
            float var16 = MathHelper.sqrt_double(velocityX * velocityX + velocityZ * velocityZ);
            yaw = (float)(java.lang.Math.atan2(velocityX, velocityZ) * 180.0D / (double)((float)Math.PI));

            for (pitch = (float)(java.lang.Math.atan2(velocityY, (double)var16) * 180.0D / (double)((float)Math.PI)); pitch - prevPitch < -180.0F; prevPitch -= 360.0F)
            {
            }

            while (pitch - prevPitch >= 180.0F)
            {
                prevPitch += 360.0F;
            }

            while (yaw - prevYaw < -180.0F)
            {
                prevYaw -= 360.0F;
            }

            while (yaw - prevYaw >= 180.0F)
            {
                prevYaw += 360.0F;
            }

            pitch = prevPitch + (pitch - prevPitch) * 0.2F;
            yaw = prevYaw + (yaw - prevYaw) * 0.2F;
            float var17 = 0.95F;
            if (isInWater())
            {
                for (int var18 = 0; var18 < 4; ++var18)
                {
                    float var19 = 0.25F;
                    world.addParticle("bubble", x - velocityX * (double)var19, y - velocityY * (double)var19, z - velocityZ * (double)var19, velocityX, velocityY, velocityZ);
                }

                var17 = 0.8F;
            }

            velocityX += field_9405_b;
            velocityY += field_9404_c;
            velocityZ += field_9403_d;
            velocityX *= (double)var17;
            velocityY *= (double)var17;
            velocityZ *= (double)var17;
            world.addParticle("smoke", x, y + 0.5D, z, 0.0D, 0.0D, 0.0D);
            setPosition(x, y, z);
        }

        public override void writeNbt(NBTTagCompound var1)
        {
            var1.setShort("xTile", (short)field_9402_e);
            var1.setShort("yTile", (short)field_9401_f);
            var1.setShort("zTile", (short)field_9400_g);
            var1.setByte("inTile", (sbyte)field_9399_h);
            var1.setByte("shake", (sbyte)field_9406_a);
            var1.setByte("inGround", (sbyte)(field_9398_i ? 1 : 0));
        }

        public override void readNbt(NBTTagCompound var1)
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

        public override bool damage(Entity var1, int var2)
        {
            setBeenAttacked();
            if (var1 != null)
            {
                Vec3D var3 = var1.getLookVec();
                if (var3 != null)
                {
                    velocityX = var3.xCoord;
                    velocityY = var3.yCoord;
                    velocityZ = var3.zCoord;
                    field_9405_b = velocityX * 0.1D;
                    field_9404_c = velocityY * 0.1D;
                    field_9403_d = velocityZ * 0.1D;
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        public override float getShadowRadius()
        {
            return 0.0F;
        }
    }

}