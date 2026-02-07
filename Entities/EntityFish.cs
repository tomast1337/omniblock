using betareborn.Items;
using betareborn.Materials;
using betareborn.NBT;
using betareborn.Stats;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityFish : Entity
    {

        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityFish).TypeHandle);
        private int xTile;
        private int yTile;
        private int zTile;
        private int inTile;
        private bool inGround;
        public int shake;
        public EntityPlayer angler;
        private int ticksInGround;
        private int ticksInAir;
        private int ticksCatchable;
        public Entity bobber;
        private int field_6388_l;
        private double field_6387_m;
        private double field_6386_n;
        private double field_6385_o;
        private double field_6384_p;
        private double field_6383_q;
        private double velocityX;
        private double velocityY;
        private double velocityZ;

        public EntityFish(World var1) : base(var1)
        {
            xTile = -1;
            yTile = -1;
            zTile = -1;
            inTile = 0;
            inGround = false;
            shake = 0;
            ticksInAir = 0;
            ticksCatchable = 0;
            bobber = null;
            setSize(0.25F, 0.25F);
            ignoreFrustumCheck = true;
        }

        public EntityFish(World var1, double var2, double var4, double var6) : this(var1)
        {
            setPosition(var2, var4, var6);
            ignoreFrustumCheck = true;
        }

        public EntityFish(World var1, EntityPlayer var2) : base(var1)
        {
            xTile = -1;
            yTile = -1;
            zTile = -1;
            inTile = 0;
            inGround = false;
            shake = 0;
            ticksInAir = 0;
            ticksCatchable = 0;
            bobber = null;
            ignoreFrustumCheck = true;
            angler = var2;
            angler.fishEntity = this;
            setSize(0.25F, 0.25F);
            setLocationAndAngles(var2.posX, var2.posY + 1.62D - (double)var2.yOffset, var2.posZ, var2.rotationYaw, var2.rotationPitch);
            posX -= (double)(MathHelper.cos(rotationYaw / 180.0F * (float)java.lang.Math.PI) * 0.16F);
            posY -= (double)0.1F;
            posZ -= (double)(MathHelper.sin(rotationYaw / 180.0F * (float)java.lang.Math.PI) * 0.16F);
            setPosition(posX, posY, posZ);
            yOffset = 0.0F;
            float var3 = 0.4F;
            motionX = (double)(-MathHelper.sin(rotationYaw / 180.0F * (float)java.lang.Math.PI) * MathHelper.cos(rotationPitch / 180.0F * (float)java.lang.Math.PI) * var3);
            motionZ = (double)(MathHelper.cos(rotationYaw / 180.0F * (float)java.lang.Math.PI) * MathHelper.cos(rotationPitch / 180.0F * (float)java.lang.Math.PI) * var3);
            motionY = (double)(-MathHelper.sin(rotationPitch / 180.0F * (float)java.lang.Math.PI) * var3);
            func_4042_a(motionX, motionY, motionZ, 1.5F, 1.0F);
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

        public void func_4042_a(double var1, double var3, double var5, float var7, float var8)
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
            ticksInGround = 0;
        }

        public override void setPositionAndRotation2(double var1, double var3, double var5, float var7, float var8, int var9)
        {
            field_6387_m = var1;
            field_6386_n = var3;
            field_6385_o = var5;
            field_6384_p = (double)var7;
            field_6383_q = (double)var8;
            field_6388_l = var9;
            motionX = velocityX;
            motionY = velocityY;
            motionZ = velocityZ;
        }

        public override void setVelocity(double var1, double var3, double var5)
        {
            velocityX = motionX = var1;
            velocityY = motionY = var3;
            velocityZ = motionZ = var5;
        }

        public override void onUpdate()
        {
            base.onUpdate();
            if (field_6388_l > 0)
            {
                double var21 = posX + (field_6387_m - posX) / (double)field_6388_l;
                double var22 = posY + (field_6386_n - posY) / (double)field_6388_l;
                double var23 = posZ + (field_6385_o - posZ) / (double)field_6388_l;

                double var7;
                for (var7 = field_6384_p - (double)rotationYaw; var7 < -180.0D; var7 += 360.0D)
                {
                }

                while (var7 >= 180.0D)
                {
                    var7 -= 360.0D;
                }

                rotationYaw = (float)((double)rotationYaw + var7 / (double)field_6388_l);
                rotationPitch = (float)((double)rotationPitch + (field_6383_q - (double)rotationPitch) / (double)field_6388_l);
                --field_6388_l;
                setPosition(var21, var22, var23);
                setRotation(rotationYaw, rotationPitch);
            }
            else
            {
                if (!worldObj.multiplayerWorld)
                {
                    ItemStack var1 = angler.getCurrentEquippedItem();
                    if (angler.isDead || !angler.isEntityAlive() || var1 == null || var1.getItem() != Item.fishingRod || getDistanceSqToEntity(angler) > 1024.0D)
                    {
                        setEntityDead();
                        angler.fishEntity = null;
                        return;
                    }

                    if (bobber != null)
                    {
                        if (!bobber.isDead)
                        {
                            posX = bobber.posX;
                            posY = bobber.boundingBox.minY + (double)bobber.height * 0.8D;
                            posZ = bobber.posZ;
                            return;
                        }

                        bobber = null;
                    }
                }

                if (shake > 0)
                {
                    --shake;
                }

                if (inGround)
                {
                    int var19 = worldObj.getBlockId(xTile, yTile, zTile);
                    if (var19 == inTile)
                    {
                        ++ticksInGround;
                        if (ticksInGround == 1200)
                        {
                            setEntityDead();
                        }

                        return;
                    }

                    inGround = false;
                    motionX *= (double)(rand.nextFloat() * 0.2F);
                    motionY *= (double)(rand.nextFloat() * 0.2F);
                    motionZ *= (double)(rand.nextFloat() * 0.2F);
                    ticksInGround = 0;
                    ticksInAir = 0;
                }
                else
                {
                    ++ticksInAir;
                }

                Vec3D var20 = Vec3D.createVector(posX, posY, posZ);
                Vec3D var2 = Vec3D.createVector(posX + motionX, posY + motionY, posZ + motionZ);
                MovingObjectPosition var3 = worldObj.rayTraceBlocks(var20, var2);
                var20 = Vec3D.createVector(posX, posY, posZ);
                var2 = Vec3D.createVector(posX + motionX, posY + motionY, posZ + motionZ);
                if (var3 != null)
                {
                    var2 = Vec3D.createVector(var3.hitVec.xCoord, var3.hitVec.yCoord, var3.hitVec.zCoord);
                }

                Entity var4 = null;
                var var5 = worldObj.getEntitiesWithinAABBExcludingEntity(this, boundingBox.stretch(motionX, motionY, motionZ).expand(1.0D, 1.0D, 1.0D));
                double var6 = 0.0D;

                double var13;
                for (int var8 = 0; var8 < var5.Count; ++var8)
                {
                    Entity var9 = var5[var8];
                    if (var9.canBeCollidedWith() && (var9 != angler || ticksInAir >= 5))
                    {
                        float var10 = 0.3F;
                        Box var11 = var9.boundingBox.expand((double)var10, (double)var10, (double)var10);
                        MovingObjectPosition var12 = var11.raycast(var20, var2);
                        if (var12 != null)
                        {
                            var13 = var20.distanceTo(var12.hitVec);
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
                    if (var3.entityHit != null)
                    {
                        if (var3.entityHit.attackEntityFrom(angler, 0))
                        {
                            bobber = var3.entityHit;
                        }
                    }
                    else
                    {
                        inGround = true;
                    }
                }

                if (!inGround)
                {
                    moveEntity(motionX, motionY, motionZ);
                    float var24 = MathHelper.sqrt_double(motionX * motionX + motionZ * motionZ);
                    rotationYaw = (float)(java.lang.Math.atan2(motionX, motionZ) * 180.0D / (double)((float)java.lang.Math.PI));

                    for (rotationPitch = (float)(java.lang.Math.atan2(motionY, (double)var24) * 180.0D / (double)((float)java.lang.Math.PI)); rotationPitch - prevRotationPitch < -180.0F; prevRotationPitch -= 360.0F)
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
                    float var25 = 0.92F;
                    if (onGround || isCollidedHorizontally)
                    {
                        var25 = 0.5F;
                    }

                    byte var26 = 5;
                    double var27 = 0.0D;

                    for (int var28 = 0; var28 < var26; ++var28)
                    {
                        double var14 = boundingBox.minY + (boundingBox.maxY - boundingBox.minY) * (double)(var28 + 0) / (double)var26 - 0.125D + 0.125D;
                        double var16 = boundingBox.minY + (boundingBox.maxY - boundingBox.minY) * (double)(var28 + 1) / (double)var26 - 0.125D + 0.125D;
                        Box var18 = Box.createCached(boundingBox.minX, var14, boundingBox.minZ, boundingBox.maxX, var16, boundingBox.maxZ);
                        if (worldObj.isAABBInMaterial(var18, Material.water))
                        {
                            var27 += 1.0D / (double)var26;
                        }
                    }

                    if (var27 > 0.0D)
                    {
                        if (ticksCatchable > 0)
                        {
                            --ticksCatchable;
                        }
                        else
                        {
                            short var29 = 500;
                            if (worldObj.canBlockBeRainedOn(MathHelper.floor_double(posX), MathHelper.floor_double(posY) + 1, MathHelper.floor_double(posZ)))
                            {
                                var29 = 300;
                            }

                            if (rand.nextInt(var29) == 0)
                            {
                                ticksCatchable = rand.nextInt(30) + 10;
                                motionY -= (double)0.2F;
                                worldObj.playSoundAtEntity(this, "random.splash", 0.25F, 1.0F + (rand.nextFloat() - rand.nextFloat()) * 0.4F);
                                float var30 = (float)MathHelper.floor_double(boundingBox.minY);

                                int var15;
                                float var17;
                                float var31;
                                for (var15 = 0; (float)var15 < 1.0F + width * 20.0F; ++var15)
                                {
                                    var31 = (rand.nextFloat() * 2.0F - 1.0F) * width;
                                    var17 = (rand.nextFloat() * 2.0F - 1.0F) * width;
                                    worldObj.addParticle("bubble", posX + (double)var31, (double)(var30 + 1.0F), posZ + (double)var17, motionX, motionY - (double)(rand.nextFloat() * 0.2F), motionZ);
                                }

                                for (var15 = 0; (float)var15 < 1.0F + width * 20.0F; ++var15)
                                {
                                    var31 = (rand.nextFloat() * 2.0F - 1.0F) * width;
                                    var17 = (rand.nextFloat() * 2.0F - 1.0F) * width;
                                    worldObj.addParticle("splash", posX + (double)var31, (double)(var30 + 1.0F), posZ + (double)var17, motionX, motionY, motionZ);
                                }
                            }
                        }
                    }

                    if (ticksCatchable > 0)
                    {
                        motionY -= (double)(rand.nextFloat() * rand.nextFloat() * rand.nextFloat()) * 0.2D;
                    }

                    var13 = var27 * 2.0D - 1.0D;
                    motionY += (double)0.04F * var13;
                    if (var27 > 0.0D)
                    {
                        var25 = (float)((double)var25 * 0.9D);
                        motionY *= 0.8D;
                    }

                    motionX *= (double)var25;
                    motionY *= (double)var25;
                    motionZ *= (double)var25;
                    setPosition(posX, posY, posZ);
                }
            }
        }

        public override void writeEntityToNBT(NBTTagCompound var1)
        {
            var1.setShort("xTile", (short)xTile);
            var1.setShort("yTile", (short)yTile);
            var1.setShort("zTile", (short)zTile);
            var1.setByte("inTile", (sbyte)inTile);
            var1.setByte("shake", (sbyte)shake);
            var1.setByte("inGround", (sbyte)(inGround ? 1 : 0));
        }

        public override void readEntityFromNBT(NBTTagCompound var1)
        {
            xTile = var1.getShort("xTile");
            yTile = var1.getShort("yTile");
            zTile = var1.getShort("zTile");
            inTile = var1.getByte("inTile") & 255;
            shake = var1.getByte("shake") & 255;
            inGround = var1.getByte("inGround") == 1;
        }

        public override float getShadowSize()
        {
            return 0.0F;
        }

        public int catchFish()
        {
            byte var1 = 0;
            if (bobber != null)
            {
                double var2 = angler.posX - posX;
                double var4 = angler.posY - posY;
                double var6 = angler.posZ - posZ;
                double var8 = (double)MathHelper.sqrt_double(var2 * var2 + var4 * var4 + var6 * var6);
                double var10 = 0.1D;
                bobber.motionX += var2 * var10;
                bobber.motionY += var4 * var10 + (double)MathHelper.sqrt_double(var8) * 0.08D;
                bobber.motionZ += var6 * var10;
                var1 = 3;
            }
            else if (ticksCatchable > 0)
            {
                EntityItem var13 = new EntityItem(worldObj, posX, posY, posZ, new ItemStack(Item.fishRaw));
                double var3 = angler.posX - posX;
                double var5 = angler.posY - posY;
                double var7 = angler.posZ - posZ;
                double var9 = (double)MathHelper.sqrt_double(var3 * var3 + var5 * var5 + var7 * var7);
                double var11 = 0.1D;
                var13.motionX = var3 * var11;
                var13.motionY = var5 * var11 + (double)MathHelper.sqrt_double(var9) * 0.08D;
                var13.motionZ = var7 * var11;
                worldObj.entityJoinedWorld(var13);
                angler.addStat(StatList.fishCaughtStat, 1);
                var1 = 1;
            }

            if (inGround)
            {
                var1 = 2;
            }

            setEntityDead();
            angler.fishEntity = null;
            return var1;
        }
    }

}