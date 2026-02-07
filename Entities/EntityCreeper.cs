using betareborn.Items;
using betareborn.NBT;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntityCreeper : EntityMob
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityCreeper).TypeHandle);

        int timeSinceIgnited;
        int lastActiveTime;


        public EntityCreeper(World var1) : base(var1)
        {
            texture = "/mob/creeper.png";
        }

        protected override void entityInit()
        {
            base.entityInit();
            dataWatcher.addObject(16, java.lang.Byte.valueOf(255)); // -1
            dataWatcher.addObject(17, java.lang.Byte.valueOf(0));
        }

        public override void writeEntityToNBT(NBTTagCompound var1)
        {
            base.writeEntityToNBT(var1);
            if (dataWatcher.getWatchableObjectByte(17) == 1)
            {
                var1.setBoolean("powered", true);
            }

        }

        public override void readEntityFromNBT(NBTTagCompound var1)
        {
            base.readEntityFromNBT(var1);
            dataWatcher.updateObject(17, java.lang.Byte.valueOf((byte)(var1.getBoolean("powered") ? 1 : 0)));
        }

        protected override void attackBlockedEntity(Entity var1, float var2)
        {
            if (!worldObj.multiplayerWorld)
            {
                if (timeSinceIgnited > 0)
                {
                    setCreeperState(-1);
                    --timeSinceIgnited;
                    if (timeSinceIgnited < 0)
                    {
                        timeSinceIgnited = 0;
                    }
                }

            }
        }

        public override void onUpdate()
        {
            lastActiveTime = timeSinceIgnited;
            if (worldObj.multiplayerWorld)
            {
                int var1 = getCreeperState();
                if (var1 > 0 && timeSinceIgnited == 0)
                {
                    worldObj.playSoundAtEntity(this, "random.fuse", 1.0F, 0.5F);
                }

                timeSinceIgnited += var1;
                if (timeSinceIgnited < 0)
                {
                    timeSinceIgnited = 0;
                }

                if (timeSinceIgnited >= 30)
                {
                    timeSinceIgnited = 30;
                }
            }

            base.onUpdate();
            if (playerToAttack == null && timeSinceIgnited > 0)
            {
                setCreeperState(-1);
                --timeSinceIgnited;
                if (timeSinceIgnited < 0)
                {
                    timeSinceIgnited = 0;
                }
            }

        }

        protected override string getHurtSound()
        {
            return "mob.creeper";
        }

        protected override string getDeathSound()
        {
            return "mob.creeperdeath";
        }

        public override void onDeath(Entity var1)
        {
            base.onDeath(var1);
            if (var1 is EntitySkeleton)
            {
                dropItem(Item.record13.id + rand.nextInt(2), 1);
            }

        }

        protected override void attackEntity(Entity var1, float var2)
        {
            if (!worldObj.multiplayerWorld)
            {
                int var3 = getCreeperState();
                if (var3 <= 0 && var2 < 3.0F || var3 > 0 && var2 < 7.0F)
                {
                    if (timeSinceIgnited == 0)
                    {
                        worldObj.playSoundAtEntity(this, "random.fuse", 1.0F, 0.5F);
                    }

                    setCreeperState(1);
                    ++timeSinceIgnited;
                    if (timeSinceIgnited >= 30)
                    {
                        if (getPowered())
                        {
                            worldObj.createExplosion(this, posX, posY, posZ, 6.0F);
                        }
                        else
                        {
                            worldObj.createExplosion(this, posX, posY, posZ, 3.0F);
                        }

                        setEntityDead();
                    }

                    hasAttacked = true;
                }
                else
                {
                    setCreeperState(-1);
                    --timeSinceIgnited;
                    if (timeSinceIgnited < 0)
                    {
                        timeSinceIgnited = 0;
                    }
                }

            }
        }

        public bool getPowered()
        {
            return dataWatcher.getWatchableObjectByte(17) == 1;
        }

        public float setCreeperFlashTime(float var1)
        {
            return ((float)lastActiveTime + (float)(timeSinceIgnited - lastActiveTime) * var1) / 28.0F;
        }

        protected override int getDropItemId()
        {
            return Item.gunpowder.id;
        }

        private int getCreeperState()
        {
            return dataWatcher.getWatchableObjectByte(16);
        }

        private void setCreeperState(int var1)
        {
            dataWatcher.updateObject(16, java.lang.Byte.valueOf((byte)var1));
        }

        public override void onStruckByLightning(EntityLightningBolt var1)
        {
            base.onStruckByLightning(var1);
            dataWatcher.updateObject(17, java.lang.Byte.valueOf(1));
        }
    }
}