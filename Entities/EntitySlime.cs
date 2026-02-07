using betareborn.Chunks;
using betareborn.Items;
using betareborn.NBT;
using betareborn.Worlds;

namespace betareborn.Entities
{
    public class EntitySlime : EntityLiving, IMob
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntitySlime).TypeHandle);

        public float field_768_a;
        public float field_767_b;
        private int slimeJumpDelay = 0;

        public EntitySlime(World var1) : base(var1)
        {
            texture = "/mob/slime.png";
            int var2 = 1 << rand.nextInt(3);
            yOffset = 0.0F;
            slimeJumpDelay = rand.nextInt(20) + 10;
            setSlimeSize(var2);
        }

        protected override void entityInit()
        {
            base.entityInit();
            dataWatcher.addObject(16, new java.lang.Byte((byte)1));
        }

        public void setSlimeSize(int var1)
        {
            dataWatcher.updateObject(16, new java.lang.Byte((byte)var1));
            setSize(0.6F * (float)var1, 0.6F * (float)var1);
            health = var1 * var1;
            setPosition(posX, posY, posZ);
        }

        public int getSlimeSize()
        {
            return dataWatcher.getWatchableObjectByte(16);
        }

        public override void writeEntityToNBT(NBTTagCompound var1)
        {
            base.writeEntityToNBT(var1);
            var1.setInteger("Size", getSlimeSize() - 1);
        }

        public override void readEntityFromNBT(NBTTagCompound var1)
        {
            base.readEntityFromNBT(var1);
            setSlimeSize(var1.getInteger("Size") + 1);
        }

        public override void onUpdate()
        {
            field_767_b = field_768_a;
            bool var1 = onGround;
            base.onUpdate();
            if (onGround && !var1)
            {
                int var2 = getSlimeSize();

                for (int var3 = 0; var3 < var2 * 8; ++var3)
                {
                    float var4 = rand.nextFloat() * (float)Math.PI * 2.0F;
                    float var5 = rand.nextFloat() * 0.5F + 0.5F;
                    float var6 = MathHelper.sin(var4) * (float)var2 * 0.5F * var5;
                    float var7 = MathHelper.cos(var4) * (float)var2 * 0.5F * var5;
                    worldObj.addParticle("slime", posX + (double)var6, boundingBox.minY, posZ + (double)var7, 0.0D, 0.0D, 0.0D);
                }

                if (var2 > 2)
                {
                    worldObj.playSoundAtEntity(this, "mob.slime", getSoundVolume(), ((rand.nextFloat() - rand.nextFloat()) * 0.2F + 1.0F) / 0.8F);
                }

                field_768_a = -0.5F;
            }

            field_768_a *= 0.6F;
        }

        public override void updatePlayerActionState()
        {
            func_27021_X();
            EntityPlayer var1 = worldObj.getClosestPlayerToEntity(this, 16.0D);
            if (var1 != null)
            {
                faceEntity(var1, 10.0F, 20.0F);
            }

            if (onGround && slimeJumpDelay-- <= 0)
            {
                slimeJumpDelay = rand.nextInt(20) + 10;
                if (var1 != null)
                {
                    slimeJumpDelay /= 3;
                }

                isJumping = true;
                if (getSlimeSize() > 1)
                {
                    worldObj.playSoundAtEntity(this, "mob.slime", getSoundVolume(), ((rand.nextFloat() - rand.nextFloat()) * 0.2F + 1.0F) * 0.8F);
                }

                field_768_a = 1.0F;
                moveStrafing = 1.0F - rand.nextFloat() * 2.0F;
                moveForward = (float)(1 * getSlimeSize());
            }
            else
            {
                isJumping = false;
                if (onGround)
                {
                    moveStrafing = moveForward = 0.0F;
                }
            }

        }

        public override void setEntityDead()
        {
            int var1 = getSlimeSize();
            if (!worldObj.multiplayerWorld && var1 > 1 && health == 0)
            {
                for (int var2 = 0; var2 < 4; ++var2)
                {
                    float var3 = ((float)(var2 % 2) - 0.5F) * (float)var1 / 4.0F;
                    float var4 = ((float)(var2 / 2) - 0.5F) * (float)var1 / 4.0F;
                    EntitySlime var5 = new EntitySlime(worldObj);
                    var5.setSlimeSize(var1 / 2);
                    var5.setLocationAndAngles(posX + (double)var3, posY + 0.5D, posZ + (double)var4, rand.nextFloat() * 360.0F, 0.0F);
                    worldObj.entityJoinedWorld(var5);
                }
            }

            base.setEntityDead();
        }

        public override void onCollideWithPlayer(EntityPlayer var1)
        {
            int var2 = getSlimeSize();
            if (var2 > 1 && canEntityBeSeen(var1) && (double)getDistanceToEntity(var1) < 0.6D * (double)var2 && var1.attackEntityFrom(this, var2))
            {
                worldObj.playSoundAtEntity(this, "mob.slimeattack", 1.0F, (rand.nextFloat() - rand.nextFloat()) * 0.2F + 1.0F);
            }

        }

        protected override String getHurtSound()
        {
            return "mob.slime";
        }

        protected override String getDeathSound()
        {
            return "mob.slime";
        }

        protected override int getDropItemId()
        {
            return getSlimeSize() == 1 ? Item.slimeBall.shiftedIndex : 0;
        }

        public override bool getCanSpawnHere()
        {
            Chunk var1 = worldObj.getChunkFromBlockCoords(MathHelper.floor_double(posX), MathHelper.floor_double(posZ));
            return (getSlimeSize() == 1 || worldObj.difficultySetting > 0) && rand.nextInt(10) == 0 && var1.func_997_a(987234911L).nextInt(10) == 0 && posY < 16.0D;
        }

        protected override float getSoundVolume()
        {
            return 0.6F;
        }
    }

}