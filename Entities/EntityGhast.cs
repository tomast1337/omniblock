using betareborn.Items;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityGhast : EntityFlying, IMob
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityGhast).TypeHandle);

        public int courseChangeCooldown = 0;
        public double waypointX;
        public double waypointY;
        public double waypointZ;
        private Entity targetedEntity = null;
        private int aggroCooldown = 0;
        public int prevAttackCounter = 0;
        public int attackCounter = 0;

        public EntityGhast(World var1) : base(var1)
        {
            texture = "/mob/ghast.png";
            setSize(4.0F, 4.0F);
            isImmuneToFire = true;
        }

        protected override void entityInit()
        {
            base.entityInit();
            dataWatcher.addObject(16, java.lang.Byte.valueOf((byte)0));
        }

        public override void onUpdate()
        {
            base.onUpdate();
            sbyte var1 = dataWatcher.getWatchableObjectByte(16);
            texture = var1 == 1 ? "/mob/ghast_fire.png" : "/mob/ghast.png";
        }

        public override void updatePlayerActionState()
        {
            if (!worldObj.multiplayerWorld && worldObj.difficultySetting == 0)
            {
                setEntityDead();
            }

            func_27021_X();
            prevAttackCounter = attackCounter;
            double var1 = waypointX - posX;
            double var3 = waypointY - posY;
            double var5 = waypointZ - posZ;
            double var7 = (double)MathHelper.sqrt_double(var1 * var1 + var3 * var3 + var5 * var5);
            if (var7 < 1.0D || var7 > 60.0D)
            {
                waypointX = posX + (double)((rand.nextFloat() * 2.0F - 1.0F) * 16.0F);
                waypointY = posY + (double)((rand.nextFloat() * 2.0F - 1.0F) * 16.0F);
                waypointZ = posZ + (double)((rand.nextFloat() * 2.0F - 1.0F) * 16.0F);
            }

            if (courseChangeCooldown-- <= 0)
            {
                courseChangeCooldown += rand.nextInt(5) + 2;
                if (isCourseTraversable(waypointX, waypointY, waypointZ, var7))
                {
                    motionX += var1 / var7 * 0.1D;
                    motionY += var3 / var7 * 0.1D;
                    motionZ += var5 / var7 * 0.1D;
                }
                else
                {
                    waypointX = posX;
                    waypointY = posY;
                    waypointZ = posZ;
                }
            }

            if (targetedEntity != null && targetedEntity.isDead)
            {
                targetedEntity = null;
            }

            if (targetedEntity == null || aggroCooldown-- <= 0)
            {
                targetedEntity = worldObj.getClosestPlayerToEntity(this, 100.0D);
                if (targetedEntity != null)
                {
                    aggroCooldown = 20;
                }
            }

            double var9 = 64.0D;
            if (targetedEntity != null && targetedEntity.getDistanceSqToEntity(this) < var9 * var9)
            {
                double var11 = targetedEntity.posX - posX;
                double var13 = targetedEntity.boundingBox.minY + (double)(targetedEntity.height / 2.0F) - (posY + (double)(height / 2.0F));
                double var15 = targetedEntity.posZ - posZ;
                renderYawOffset = rotationYaw = -((float)java.lang.Math.atan2(var11, var15)) * 180.0F / (float)java.lang.Math.PI;
                if (canEntityBeSeen(targetedEntity))
                {
                    if (attackCounter == 10)
                    {
                        worldObj.playSoundAtEntity(this, "mob.ghast.charge", getSoundVolume(), (rand.nextFloat() - rand.nextFloat()) * 0.2F + 1.0F);
                    }

                    ++attackCounter;
                    if (attackCounter == 20)
                    {
                        worldObj.playSoundAtEntity(this, "mob.ghast.fireball", getSoundVolume(), (rand.nextFloat() - rand.nextFloat()) * 0.2F + 1.0F);
                        EntityFireball var17 = new EntityFireball(worldObj, this, var11, var13, var15);
                        double var18 = 4.0D;
                        Vec3D var20 = getLook(1.0F);
                        var17.posX = posX + var20.xCoord * var18;
                        var17.posY = posY + (double)(height / 2.0F) + 0.5D;
                        var17.posZ = posZ + var20.zCoord * var18;
                        worldObj.entityJoinedWorld(var17);
                        attackCounter = -40;
                    }
                }
                else if (attackCounter > 0)
                {
                    --attackCounter;
                }
            }
            else
            {
                renderYawOffset = rotationYaw = -((float)java.lang.Math.atan2(motionX, motionZ)) * 180.0F / (float)java.lang.Math.PI;
                if (attackCounter > 0)
                {
                    --attackCounter;
                }
            }

            if (!worldObj.multiplayerWorld)
            {
                sbyte var21 = dataWatcher.getWatchableObjectByte(16);
                byte var12 = (byte)(attackCounter > 10 ? 1 : 0);
                if (var21 != var12)
                {
                    dataWatcher.updateObject(16, java.lang.Byte.valueOf(var12));
                }
            }

        }

        private bool isCourseTraversable(double var1, double var3, double var5, double var7)
        {
            double var9 = (waypointX - posX) / var7;
            double var11 = (waypointY - posY) / var7;
            double var13 = (waypointZ - posZ) / var7;
            Box var15 = boundingBox.copy();

            for (int var16 = 1; (double)var16 < var7; ++var16)
            {
                var15.translate(var9, var11, var13);
                if (worldObj.getCollidingBoundingBoxes(this, var15).Count > 0)
                {
                    return false;
                }
            }

            return true;
        }

        protected override String getLivingSound()
        {
            return "mob.ghast.moan";
        }

        protected override String getHurtSound()
        {
            return "mob.ghast.scream";
        }

        protected override String getDeathSound()
        {
            return "mob.ghast.death";
        }

        protected override int getDropItemId()
        {
            return Item.gunpowder.shiftedIndex;
        }

        protected override float getSoundVolume()
        {
            return 10.0F;
        }

        public override bool getCanSpawnHere()
        {
            return rand.nextInt(20) == 0 && base.getCanSpawnHere() && worldObj.difficultySetting > 0;
        }

        public override int getMaxSpawnedInChunk()
        {
            return 1;
        }
    }

}