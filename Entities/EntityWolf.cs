using betareborn.Items;
using betareborn.NBT;
using betareborn.Worlds;
using java.lang;

namespace betareborn.Entities
{
    public class EntityWolf : EntityAnimal
    {
        public static readonly new Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityWolf).TypeHandle);
        private bool looksWithInterest = false;
        private float field_25048_b;
        private float field_25054_c;
        private bool isWolfShaking;
        private bool field_25052_g;
        private float timeWolfIsShaking;
        private float prevTimeWolfIsShaking;

        public EntityWolf(World var1) : base(var1)
        {
            texture = "/mob/wolf.png";
            setSize(0.8F, 0.8F);
            moveSpeed = 1.1F;
            health = 8;
        }

        protected override void entityInit()
        {
            base.entityInit();
            dataWatcher.addObject(16, java.lang.Byte.valueOf((byte)0));
            dataWatcher.addObject(17, new JString(""));
            dataWatcher.addObject(18, new java.lang.Integer(health));
        }

        protected override bool canTriggerWalking()
        {
            return false;
        }

        public override string getEntityTexture()
        {
            return isWolfTamed() ? "/mob/wolf_tame.png" : (isWolfAngry() ? "/mob/wolf_angry.png" : base.getEntityTexture());
        }

        public override void writeEntityToNBT(NBTTagCompound var1)
        {
            base.writeEntityToNBT(var1);
            var1.setBoolean("Angry", isWolfAngry());
            var1.setBoolean("Sitting", isWolfSitting());
            if (getWolfOwner() == null)
            {
                var1.setString("Owner", "");
            }
            else
            {
                var1.setString("Owner", getWolfOwner());
            }

        }

        public override void readEntityFromNBT(NBTTagCompound var1)
        {
            base.readEntityFromNBT(var1);
            setWolfAngry(var1.getBoolean("Angry"));
            setWolfSitting(var1.getBoolean("Sitting"));
            string var2 = var1.getString("Owner");
            if (var2.Length > 0)
            {
                setWolfOwner(var2);
                setWolfTamed(true);
            }

        }

        protected override bool canDespawn()
        {
            return !isWolfTamed();
        }

        protected override string getLivingSound()
        {
            return isWolfAngry() ? "mob.wolf.growl" : (rand.nextInt(3) == 0 ? (isWolfTamed() && dataWatcher.getWatchableObjectInt(18) < 10 ? "mob.wolf.whine" : "mob.wolf.panting") : "mob.wolf.bark");
        }

        protected override string getHurtSound()
        {
            return "mob.wolf.hurt";
        }

        protected override string getDeathSound()
        {
            return "mob.wolf.death";
        }

        protected override float getSoundVolume()
        {
            return 0.4F;
        }

        protected override int getDropItemId()
        {
            return -1;
        }

        public override void updatePlayerActionState()
        {
            base.updatePlayerActionState();
            if (!hasAttacked && !hasPath() && isWolfTamed() && ridingEntity == null)
            {
                EntityPlayer var3 = worldObj.getPlayerEntityByName(getWolfOwner());
                if (var3 != null)
                {
                    float var2 = var3.getDistanceToEntity(this);
                    if (var2 > 5.0F)
                    {
                        getPathOrWalkableBlock(var3, var2);
                    }
                }
                else if (!isInWater())
                {
                    setWolfSitting(true);
                }
            }
            else if (playerToAttack == null && !hasPath() && !isWolfTamed() && worldObj.rand.nextInt(100) == 0)
            {
                var var1 = worldObj.getEntitiesWithinAABB(EntitySheep.Class, Box.createCached(posX, posY, posZ, posX + 1.0D, posY + 1.0D, posZ + 1.0D).expand(16.0D, 4.0D, 16.0D));
                if (var1.Count > 0)
                {
                    setTarget(var1[worldObj.rand.nextInt(var1.Count)]);
                }
            }

            if (isInWater())
            {
                setWolfSitting(false);
            }

            if (!worldObj.multiplayerWorld)
            {
                dataWatcher.updateObject(18, java.lang.Integer.valueOf(health));
            }

        }

        public override void onLivingUpdate()
        {
            base.onLivingUpdate();
            looksWithInterest = false;
            if (hasCurrentTarget() && !hasPath() && !isWolfAngry())
            {
                Entity var1 = getCurrentTarget();
                if (var1 is EntityPlayer)
                {
                    EntityPlayer var2 = (EntityPlayer)var1;
                    ItemStack var3 = var2.inventory.getCurrentItem();
                    if (var3 != null)
                    {
                        if (!isWolfTamed() && var3.itemID == Item.bone.shiftedIndex)
                        {
                            looksWithInterest = true;
                        }
                        else if (isWolfTamed() && Item.itemsList[var3.itemID] is ItemFood)
                        {
                            looksWithInterest = ((ItemFood)Item.itemsList[var3.itemID]).getIsWolfsFavoriteMeat();
                        }
                    }
                }
            }

            if (!isMultiplayerEntity && isWolfShaking && !field_25052_g && !hasPath() && onGround)
            {
                field_25052_g = true;
                timeWolfIsShaking = 0.0F;
                prevTimeWolfIsShaking = 0.0F;
                worldObj.func_9425_a(this, (byte)8);
            }

        }

        public override void onUpdate()
        {
            base.onUpdate();
            field_25054_c = field_25048_b;
            if (looksWithInterest)
            {
                field_25048_b += (1.0F - field_25048_b) * 0.4F;
            }
            else
            {
                field_25048_b += (0.0F - field_25048_b) * 0.4F;
            }

            if (looksWithInterest)
            {
                numTicksToChaseTarget = 10;
            }

            if (isWet())
            {
                isWolfShaking = true;
                field_25052_g = false;
                timeWolfIsShaking = 0.0F;
                prevTimeWolfIsShaking = 0.0F;
            }
            else if ((isWolfShaking || field_25052_g) && field_25052_g)
            {
                if (timeWolfIsShaking == 0.0F)
                {
                    worldObj.playSoundAtEntity(this, "mob.wolf.shake", getSoundVolume(), (rand.nextFloat() - rand.nextFloat()) * 0.2F + 1.0F);
                }

                prevTimeWolfIsShaking = timeWolfIsShaking;
                timeWolfIsShaking += 0.05F;
                if (prevTimeWolfIsShaking >= 2.0F)
                {
                    isWolfShaking = false;
                    field_25052_g = false;
                    prevTimeWolfIsShaking = 0.0F;
                    timeWolfIsShaking = 0.0F;
                }

                if (timeWolfIsShaking > 0.4F)
                {
                    float var1 = (float)boundingBox.minY;
                    int var2 = (int)(MathHelper.sin((timeWolfIsShaking - 0.4F) * (float)java.lang.Math.PI) * 7.0F);

                    for (int var3 = 0; var3 < var2; ++var3)
                    {
                        float var4 = (rand.nextFloat() * 2.0F - 1.0F) * width * 0.5F;
                        float var5 = (rand.nextFloat() * 2.0F - 1.0F) * width * 0.5F;
                        worldObj.addParticle("splash", posX + (double)var4, (double)(var1 + 0.8F), posZ + (double)var5, motionX, motionY, motionZ);
                    }
                }
            }

        }

        public bool getWolfShaking()
        {
            return isWolfShaking;
        }

        public float getShadingWhileShaking(float var1)
        {
            return 12.0F / 16.0F + (prevTimeWolfIsShaking + (timeWolfIsShaking - prevTimeWolfIsShaking) * var1) / 2.0F * 0.25F;
        }

        public float getShakeAngle(float var1, float var2)
        {
            float var3 = (prevTimeWolfIsShaking + (timeWolfIsShaking - prevTimeWolfIsShaking) * var1 + var2) / 1.8F;
            if (var3 < 0.0F)
            {
                var3 = 0.0F;
            }
            else if (var3 > 1.0F)
            {
                var3 = 1.0F;
            }

            return MathHelper.sin(var3 * (float)java.lang.Math.PI) * MathHelper.sin(var3 * (float)java.lang.Math.PI * 11.0F) * 0.15F * (float)java.lang.Math.PI;
        }

        public float getInterestedAngle(float var1)
        {
            return (field_25054_c + (field_25048_b - field_25054_c) * var1) * 0.15F * (float)java.lang.Math.PI;
        }

        public override float getEyeHeight()
        {
            return height * 0.8F;
        }

        protected override int func_25026_x()
        {
            return isWolfSitting() ? 20 : base.func_25026_x();
        }

        private void getPathOrWalkableBlock(Entity var1, float var2)
        {
            PathEntity var3 = worldObj.getPathToEntity(this, var1, 16.0F);
            if (var3 == null && var2 > 12.0F)
            {
                int var4 = MathHelper.floor_double(var1.posX) - 2;
                int var5 = MathHelper.floor_double(var1.posZ) - 2;
                int var6 = MathHelper.floor_double(var1.boundingBox.minY);

                for (int var7 = 0; var7 <= 4; ++var7)
                {
                    for (int var8 = 0; var8 <= 4; ++var8)
                    {
                        if ((var7 < 1 || var8 < 1 || var7 > 3 || var8 > 3) && worldObj.isBlockNormalCube(var4 + var7, var6 - 1, var5 + var8) && !worldObj.isBlockNormalCube(var4 + var7, var6, var5 + var8) && !worldObj.isBlockNormalCube(var4 + var7, var6 + 1, var5 + var8))
                        {
                            setLocationAndAngles((double)((float)(var4 + var7) + 0.5F), (double)var6, (double)((float)(var5 + var8) + 0.5F), rotationYaw, rotationPitch);
                            return;
                        }
                    }
                }
            }
            else
            {
                setPathToEntity(var3);
            }

        }

        protected override bool isMovementCeased()
        {
            return isWolfSitting() || field_25052_g;
        }

        public override bool attackEntityFrom(Entity var1, int var2)
        {
            setWolfSitting(false);
            if (var1 != null && !(var1 is EntityPlayer) && !(var1 is EntityArrow))
            {
                var2 = (var2 + 1) / 2;
            }

            if (!base.attackEntityFrom((Entity)var1, var2))
            {
                return false;
            }
            else
            {
                if (!isWolfTamed() && !isWolfAngry())
                {
                    if (var1 is EntityPlayer)
                    {
                        setWolfAngry(true);
                        playerToAttack = var1;
                    }

                    if (var1 is EntityArrow && ((EntityArrow)var1).owner != null)
                    {
                        var1 = ((EntityArrow)var1).owner;
                    }

                    if (var1 is EntityLiving)
                    {
                        var var3 = worldObj.getEntitiesWithinAABB(typeof(EntityWolf), Box.createCached(posX, posY, posZ, posX + 1.0D, posY + 1.0D, posZ + 1.0D).expand(16.0D, 4.0D, 16.0D));

                        foreach (var var5 in var3)
                        {
                            EntityWolf var6 = (EntityWolf)var5;
                            if (!var6.isWolfTamed() && var6.playerToAttack == null)
                            {
                                var6.playerToAttack = var1;
                                if (var1 is EntityPlayer)
                                {

                                    var6.setWolfAngry(true);
                                }
                            }
                        }
                    }
                }
                else if (var1 != this && var1 != null)
                {
                    if (isWolfTamed() && var1 is EntityPlayer && ((EntityPlayer)var1).username.Equals(getWolfOwner(), StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    playerToAttack = (Entity)var1;
                }

                return true;
            }
        }

        protected override Entity findPlayerToAttack()
        {
            return isWolfAngry() ? worldObj.getClosestPlayerToEntity(this, 16.0D) : null;
        }

        protected override void attackEntity(Entity var1, float var2)
        {
            if (var2 > 2.0F && var2 < 6.0F && rand.nextInt(10) == 0)
            {
                if (onGround)
                {
                    double var8 = var1.posX - posX;
                    double var5 = var1.posZ - posZ;
                    float var7 = MathHelper.sqrt_double(var8 * var8 + var5 * var5);
                    motionX = var8 / (double)var7 * 0.5D * (double)0.8F + motionX * (double)0.2F;
                    motionZ = var5 / (double)var7 * 0.5D * (double)0.8F + motionZ * (double)0.2F;
                    motionY = (double)0.4F;
                }
            }
            else if ((double)var2 < 1.5D && var1.boundingBox.maxY > boundingBox.minY && var1.boundingBox.minY < boundingBox.maxY)
            {
                attackTime = 20;
                byte var3 = 2;
                if (isWolfTamed())
                {
                    var3 = 4;
                }

                var1.attackEntityFrom(this, var3);
            }

        }

        public override bool interact(EntityPlayer var1)
        {
            ItemStack var2 = var1.inventory.getCurrentItem();
            if (!isWolfTamed())
            {
                if (var2 != null && var2.itemID == Item.bone.shiftedIndex && !isWolfAngry())
                {
                    --var2.stackSize;
                    if (var2.stackSize <= 0)
                    {
                        var1.inventory.setStack(var1.inventory.currentItem, (ItemStack)null);
                    }

                    if (!worldObj.multiplayerWorld)
                    {
                        if (rand.nextInt(3) == 0)
                        {
                            setWolfTamed(true);
                            setPathToEntity((PathEntity)null);
                            setWolfSitting(true);
                            health = 20;
                            setWolfOwner(var1.username);
                            showHeartsOrSmokeFX(true);
                            worldObj.func_9425_a(this, (byte)7);
                        }
                        else
                        {
                            showHeartsOrSmokeFX(false);
                            worldObj.func_9425_a(this, (byte)6);
                        }
                    }

                    return true;
                }
            }
            else
            {
                if (var2 != null && Item.itemsList[var2.itemID] is ItemFood)
                {
                    ItemFood var3 = (ItemFood)Item.itemsList[var2.itemID];
                    if (var3.getIsWolfsFavoriteMeat() && dataWatcher.getWatchableObjectInt(18) < 20)
                    {
                        --var2.stackSize;
                        if (var2.stackSize <= 0)
                        {
                            var1.inventory.setStack(var1.inventory.currentItem, (ItemStack)null);
                        }

                        heal(((ItemFood)Item.porkRaw).getHealAmount());
                        return true;
                    }
                }

                if (var1.username.Equals(getWolfOwner(), StringComparison.OrdinalIgnoreCase))
                {
                    if (!worldObj.multiplayerWorld)
                    {
                        setWolfSitting(!isWolfSitting());
                        isJumping = false;
                        setPathToEntity((PathEntity)null);
                    }

                    return true;
                }
            }

            return false;
        }

        void showHeartsOrSmokeFX(bool var1)
        {
            string var2 = "heart";
            if (!var1)
            {
                var2 = "smoke";
            }

            for (int var3 = 0; var3 < 7; ++var3)
            {
                double var4 = rand.nextGaussian() * 0.02D;
                double var6 = rand.nextGaussian() * 0.02D;
                double var8 = rand.nextGaussian() * 0.02D;
                worldObj.addParticle(var2, posX + (double)(rand.nextFloat() * width * 2.0F) - (double)width, posY + 0.5D + (double)(rand.nextFloat() * height), posZ + (double)(rand.nextFloat() * width * 2.0F) - (double)width, var4, var6, var8);
            }

        }

        public override void handleHealthUpdate(sbyte var1)
        {
            if (var1 == 7)
            {
                showHeartsOrSmokeFX(true);
            }
            else if (var1 == 6)
            {
                showHeartsOrSmokeFX(false);
            }
            else if (var1 == 8)
            {
                field_25052_g = true;
                timeWolfIsShaking = 0.0F;
                prevTimeWolfIsShaking = 0.0F;
            }
            else
            {
                base.handleHealthUpdate(var1);
            }

        }

        public float setTailRotation()
        {
            return isWolfAngry() ? (float)java.lang.Math.PI * 0.49F : (isWolfTamed() ? (0.55F - (float)(20 - dataWatcher.getWatchableObjectInt(18)) * 0.02F) * (float)java.lang.Math.PI : (float)java.lang.Math.PI * 0.2F);
        }

        public override int getMaxSpawnedInChunk()
        {
            return 8;
        }

        public string getWolfOwner()
        {
            return dataWatcher.getWatchableObjectString(17);
        }

        public void setWolfOwner(string var1)
        {
            dataWatcher.updateObject(17, new JString(var1));
        }

        public bool isWolfSitting()
        {
            return (dataWatcher.getWatchableObjectByte(16) & 1) != 0;
        }

        public void setWolfSitting(bool var1)
        {
            sbyte var2 = dataWatcher.getWatchableObjectByte(16);
            if (var1)
            {
                dataWatcher.updateObject(16, java.lang.Byte.valueOf((byte)(var2 | 1)));
            }
            else
            {
                dataWatcher.updateObject(16, java.lang.Byte.valueOf((byte)(var2 & -2)));
            }

        }

        public bool isWolfAngry()
        {
            return (dataWatcher.getWatchableObjectByte(16) & 2) != 0;
        }

        public void setWolfAngry(bool var1)
        {
            sbyte var2 = dataWatcher.getWatchableObjectByte(16);
            if (var1)
            {
                dataWatcher.updateObject(16, java.lang.Byte.valueOf((byte)(var2 | 2)));
            }
            else
            {
                dataWatcher.updateObject(16, java.lang.Byte.valueOf((byte)(var2 & -3)));
            }

        }

        public bool isWolfTamed()
        {
            return (dataWatcher.getWatchableObjectByte(16) & 4) != 0;
        }

        public void setWolfTamed(bool var1)
        {
            sbyte var2 = dataWatcher.getWatchableObjectByte(16);
            if (var1)
            {
                dataWatcher.updateObject(16, java.lang.Byte.valueOf((byte)(var2 | 4)));
            }
            else
            {
                dataWatcher.updateObject(16, java.lang.Byte.valueOf((byte)(var2 & -5)));
            }

        }
    }

}