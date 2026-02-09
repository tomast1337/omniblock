using betareborn.Blocks;
using betareborn.Blocks.Entities;
using betareborn.Blocks.Materials;
using betareborn.Inventorys;
using betareborn.Items;
using betareborn.NBT;
using betareborn.Screens;
using betareborn.Stats;
using betareborn.Util.Maths;
using betareborn.Worlds;
using betareborn.Worlds.Chunks;
using java.lang;

namespace betareborn.Entities
{
    public abstract class EntityPlayer : EntityLiving
    {
        public static readonly new Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityPlayer).TypeHandle);
        public InventoryPlayer inventory;
        public ScreenHandler inventorySlots;
        public ScreenHandler craftingInventory;
        public byte field_9371_f = 0;
        public int score = 0;
        public float prevStepBobbingAmount;
        public float stepBobbingAmount;
        public bool isSwinging = false;
        public int swingProgressInt = 0;
        public string username;
        public int dimension;
        public string playerCloakUrl;
        public double field_20066_r;
        public double field_20065_s;
        public double field_20064_t;
        public double field_20063_u;
        public double field_20062_v;
        public double field_20061_w;
        protected bool sleeping;
        public Vec3i sleepingPos;
        private int sleepTimer;
        public float sleepOffsetX;
        public float sleepOffsetY;
        public float sleepOffsetZ;
        private Vec3i playerSpawnCoordinate;
        private Vec3i startMinecartRidingCoordinate;
        public int timeUntilPortal = 20;
        protected bool inPortal = false;
        public float timeInPortal;
        public float prevTimeInPortal;
        private int damageRemainder = 0;
        public EntityFish fishEntity = null;

        public EntityPlayer(World var1) : base(var1)
        {
            inventory = new InventoryPlayer(this);
            inventorySlots = new PlayerScreenHandler(inventory, !var1.isRemote);
            craftingInventory = inventorySlots;
            yOffset = 1.62F;
            Vec3i var2 = var1.getSpawnPoint();
            setPositionAndAnglesKeepPrevAngles((double)var2.x + 0.5D, (double)(var2.y + 1), (double)var2.z + 0.5D, 0.0F, 0.0F);
            health = 20;
            field_9351_C = "humanoid";
            field_9353_B = 180.0F;
            fireResistance = 20;
            texture = "/mob/char.png";
        }

        protected override void entityInit()
        {
            base.entityInit();
            dataWatcher.addObject(16, java.lang.Byte.valueOf((byte)0));
        }

        public override void onUpdate()
        {
            if (isSleeping())
            {
                ++sleepTimer;
                if (sleepTimer > 100)
                {
                    sleepTimer = 100;
                }

                if (!worldObj.isRemote)
                {
                    if (!isSleepingInBed())
                    {
                        wakeUp(true, true, false);
                    }
                    else if (worldObj.isDaytime())
                    {
                        wakeUp(false, true, true);
                    }
                }
            }
            else if (sleepTimer > 0)
            {
                ++sleepTimer;
                if (sleepTimer >= 110)
                {
                    sleepTimer = 0;
                }
            }

            base.onUpdate();
            if (!worldObj.isRemote && craftingInventory != null && !craftingInventory.canUse(this))
            {
                closeScreen();
                craftingInventory = inventorySlots;
            }

            field_20066_r = field_20063_u;
            field_20065_s = field_20062_v;
            field_20064_t = field_20061_w;
            double var1 = posX - field_20063_u;
            double var3 = posY - field_20062_v;
            double var5 = posZ - field_20061_w;
            double var7 = 10.0D;
            if (var1 > var7)
            {
                field_20066_r = field_20063_u = posX;
            }

            if (var5 > var7)
            {
                field_20064_t = field_20061_w = posZ;
            }

            if (var3 > var7)
            {
                field_20065_s = field_20062_v = posY;
            }

            if (var1 < -var7)
            {
                field_20066_r = field_20063_u = posX;
            }

            if (var5 < -var7)
            {
                field_20064_t = field_20061_w = posZ;
            }

            if (var3 < -var7)
            {
                field_20065_s = field_20062_v = posY;
            }

            field_20063_u += var1 * 0.25D;
            field_20061_w += var5 * 0.25D;
            field_20062_v += var3 * 0.25D;
            increaseStat(Stats.Stats.minutesPlayedStat, 1);
            if (ridingEntity == null)
            {
                startMinecartRidingCoordinate = null;
            }

        }

        protected override bool isMovementBlocked()
        {
            return health <= 0 || isSleeping();
        }

        public virtual void closeScreen()
        {
            craftingInventory = inventorySlots;
        }

        public override void updateCloak()
        {
            playerCloakUrl = "http://s3.amazonaws.com/MinecraftCloaks/" + username + ".png";
            cloakUrl = playerCloakUrl;
        }

        public override void updateRidden()
        {
            double var1 = posX;
            double var3 = posY;
            double var5 = posZ;
            base.updateRidden();
            prevStepBobbingAmount = stepBobbingAmount;
            stepBobbingAmount = 0.0F;
            increaseRidingMotionStats(posX - var1, posY - var3, posZ - var5);
        }

        public override void preparePlayerToSpawn()
        {
            yOffset = 1.62F;
            setBoundingBoxSpacing(0.6F, 1.8F);
            base.preparePlayerToSpawn();
            health = 20;
            deathTime = 0;
        }

        public override void tickLiving()
        {
            if (isSwinging)
            {
                ++swingProgressInt;
                if (swingProgressInt >= 8)
                {
                    swingProgressInt = 0;
                    isSwinging = false;
                }
            }
            else
            {
                swingProgressInt = 0;
            }

            swingProgress = (float)swingProgressInt / 8.0F;
        }

        public override void tickMovement()
        {
            if (worldObj.difficulty == 0 && health < 20 && ticksExisted % 20 * 12 == 0)
            {
                heal(1);
            }

            inventory.inventoryTick();
            prevStepBobbingAmount = stepBobbingAmount;
            base.tickMovement();
            float var1 = MathHelper.sqrt_double(motionX * motionX + motionZ * motionZ);
            float var2 = (float)java.lang.Math.atan(-motionY * (double)0.2F) * 15.0F;
            if (var1 > 0.1F)
            {
                var1 = 0.1F;
            }

            if (!onGround || health <= 0)
            {
                var1 = 0.0F;
            }

            if (onGround || health <= 0)
            {
                var2 = 0.0F;
            }

            stepBobbingAmount += (var1 - stepBobbingAmount) * 0.4F;
            tilt += (var2 - tilt) * 0.8F;
            if (health > 0)
            {
                var var3 = worldObj.getEntitiesWithinAABBExcludingEntity(this, boundingBox.expand(1.0D, 0.0D, 1.0D));
                if (var3 != null)
                {
                    for (int var4 = 0; var4 < var3.Count; ++var4)
                    {
                        Entity var5 = var3[var4];
                        if (!var5.isDead)
                        {
                            collideWithEntity(var5);
                        }
                    }
                }
            }

        }

        private void collideWithEntity(Entity entity)
        {
            entity.onCollideWithPlayer(this);
        }

        public int getScore()
        {
            return score;
        }

        public override void onKilledBy(Entity adversary)
        {
            base.onKilledBy(adversary);
            setBoundingBoxSpacing(0.2F, 0.2F);
            setPosition(posX, posY, posZ);
            motionY = (double)0.1F;
            if (username.Equals("Notch"))
            {
                dropItem(new ItemStack(Item.APPLE, 1), true);
            }

            inventory.dropInventory();
            if (adversary != null)
            {
                motionX = (double)(-MathHelper.cos((attackedAtYaw + rotationYaw) * (float)java.lang.Math.PI / 180.0F) * 0.1F);
                motionZ = (double)(-MathHelper.sin((attackedAtYaw + rotationYaw) * (float)java.lang.Math.PI / 180.0F) * 0.1F);
            }
            else
            {
                motionX = motionZ = 0.0D;
            }

            yOffset = 0.1F;
            increaseStat(Stats.Stats.deathsStat, 1);
        }

        public override void updateKilledAchievement(Entity entityKilled, int score)
        {
            this.score += score;
            if (entityKilled is EntityPlayer)
            {
                increaseStat(Stats.Stats.playerKillsStat, 1);
            }
            else
            {
                increaseStat(Stats.Stats.mobKillsStat, 1);
            }

        }

        public virtual void dropSelectedItem()
        {
            dropItem(inventory.removeStack(inventory.currentItem, 1), false);
        }

        public void dropItem(ItemStack stack)
        {
            dropItem(stack, false);
        }

        public void dropItem(ItemStack stack, bool throwRandomly)
        {
            if (stack != null)
            {
                EntityItem var3 = new EntityItem(worldObj, posX, posY - (double)0.3F + (double)getEyeHeight(), posZ, stack);
                var3.delayBeforeCanPickup = 40;
                float var4 = 0.1F;
                float var5;
                if (throwRandomly)
                {
                    var5 = rand.nextFloat() * 0.5F;
                    float var6 = rand.nextFloat() * (float)java.lang.Math.PI * 2.0F;
                    var3.motionX = (double)(-MathHelper.sin(var6) * var5);
                    var3.motionZ = (double)(MathHelper.cos(var6) * var5);
                    var3.motionY = (double)0.2F;
                }
                else
                {
                    var4 = 0.3F;
                    var3.motionX = (double)(-MathHelper.sin(rotationYaw / 180.0F * (float)java.lang.Math.PI) * MathHelper.cos(rotationPitch / 180.0F * (float)java.lang.Math.PI) * var4);
                    var3.motionZ = (double)(MathHelper.cos(rotationYaw / 180.0F * (float)java.lang.Math.PI) * MathHelper.cos(rotationPitch / 180.0F * (float)java.lang.Math.PI) * var4);
                    var3.motionY = (double)(-MathHelper.sin(rotationPitch / 180.0F * (float)java.lang.Math.PI) * var4 + 0.1F);
                    var4 = 0.02F;
                    var5 = rand.nextFloat() * (float)java.lang.Math.PI * 2.0F;
                    var4 *= rand.nextFloat();
                    var3.motionX += java.lang.Math.cos((double)var5) * (double)var4;
                    var3.motionY += (double)((rand.nextFloat() - rand.nextFloat()) * 0.1F);
                    var3.motionZ += java.lang.Math.sin((double)var5) * (double)var4;
                }

                spawnItem(var3);
                increaseStat(Stats.Stats.dropStat, 1);
            }
        }

        protected virtual void spawnItem(EntityItem itemEntity)
        {
            worldObj.spawnEntity(itemEntity);
        }

        public float getBlockBreakingSpeed(Block block)
        {
            float var2 = inventory.getStrVsBlock(block);
            if (isInsideOfMaterial(Material.WATER))
            {
                var2 /= 5.0F;
            }

            if (!onGround)
            {
                var2 /= 5.0F;
            }

            return var2;
        }

        public bool canHarvest(Block block)
        {
            return inventory.canHarvestBlock(block);
        }

        public override void readNbt(NBTTagCompound nbt)
        {
            base.readNbt(nbt);
            NBTTagList var2 = nbt.getTagList("Inventory");
            inventory.readFromNBT(var2);
            dimension = nbt.getInteger("Dimension");
            sleeping = nbt.getBoolean("Sleeping");
            sleepTimer = nbt.getShort("SleepTimer");
            if (sleeping)
            {
                sleepingPos = new Vec3i(MathHelper.floor_double(posX), MathHelper.floor_double(posY), MathHelper.floor_double(posZ));
                wakeUp(true, true, false);
            }

            if (nbt.hasKey("SpawnX") && nbt.hasKey("SpawnY") && nbt.hasKey("SpawnZ"))
            {
                playerSpawnCoordinate = new Vec3i(nbt.getInteger("SpawnX"), nbt.getInteger("SpawnY"), nbt.getInteger("SpawnZ"));
            }

        }

        public override void writeNbt(NBTTagCompound nbt)
        {
            base.writeNbt(nbt);
            nbt.setTag("Inventory", inventory.writeToNBT(new NBTTagList()));
            nbt.setInteger("Dimension", dimension);
            nbt.setBoolean("Sleeping", sleeping);
            nbt.setShort("SleepTimer", (short)sleepTimer);
            if (playerSpawnCoordinate != null)
            {
                nbt.setInteger("SpawnX", playerSpawnCoordinate.x);
                nbt.setInteger("SpawnY", playerSpawnCoordinate.y);
                nbt.setInteger("SpawnZ", playerSpawnCoordinate.z);
            }

        }

        public virtual void openChestScreen(IInventory inventory)
        {
        }

        public virtual void openCraftingScreen(int x, int y, int z)
        {
        }

        public virtual void sendPickup(Entity item, int count)
        {
        }

        public override float getEyeHeight()
        {
            return 0.12F;
        }

        protected virtual void resetEyeHeight()
        {
            yOffset = 1.62F;
        }

        public override bool damage(Entity damageSource, int amount)
        {
            entityAge = 0;
            if (health <= 0)
            {
                return false;
            }
            else
            {
                if (isSleeping() && !worldObj.isRemote)
                {
                    wakeUp(true, true, false);
                }

                if (damageSource is EntityMob || damageSource is EntityArrow)
                {
                    if (worldObj.difficulty == 0)
                    {
                        amount = 0;
                    }

                    if (worldObj.difficulty == 1)
                    {
                        amount = amount / 3 + 1;
                    }

                    if (worldObj.difficulty == 3)
                    {
                        amount = amount * 3 / 2;
                    }
                }

                if (amount == 0)
                {
                    return false;
                }
                else
                {
                    java.lang.Object var3 = damageSource;
                    if (damageSource is EntityArrow && ((EntityArrow)damageSource).owner != null)
                    {
                        var3 = ((EntityArrow)damageSource).owner;
                    }

                    if (var3 is EntityLiving)
                    {
                        commandWolvesToAttack((EntityLiving)var3, false);
                    }

                    increaseStat(Stats.Stats.damageTakenStat, amount);
                    return base.damage(damageSource, amount);
                }
            }
        }

        protected bool isPvpEnabled()
        {
            return false;
        }

        protected void commandWolvesToAttack(EntityLiving entity, bool sitting)
        {
            if (!(entity is EntityCreeper) && !(entity is EntityGhast))
            {
                if (entity is EntityWolf)
                {
                    EntityWolf var3 = (EntityWolf)entity;
                    if (var3.isWolfTamed() && username.Equals(var3.getWolfOwner()))
                    {
                        return;
                    }
                }

                if (!(entity is EntityPlayer) || isPvpEnabled())
                {
                    var var7 = worldObj.getEntitiesWithinAABB(EntityWolf.Class, new Box(posX, posY, posZ, posX + 1.0D, posY + 1.0D, posZ + 1.0D).expand(16.0D, 4.0D, 16.0D));

                    foreach (Entity var5 in var7)
                    {
                        EntityWolf var6 = (EntityWolf)var5;

                        if (!var6.isWolfTamed()) continue;
                        if (var6.getTarget() != null) continue;
                        if (!username.Equals(var6.getWolfOwner())) continue;
                        if (sitting && var6.isWolfSitting()) continue;

                        var6.setWolfSitting(false);
                        var6.setTarget(entity);
                    }
                }
            }
        }

        protected override void applyDamage(int amount)
        {
            int var2 = 25 - inventory.getTotalArmorValue();
            int var3 = amount * var2 + damageRemainder;
            inventory.damageArmor(amount);
            amount = var3 / 25;
            damageRemainder = var3 % 25;
            base.applyDamage(amount);
        }

        public virtual void openFurnaceScreen(BlockEntityFurnace furnace)
        {
        }

        public virtual void openDispenserScreen(BlockEntityDispenser dispenser)
        {
        }

        public virtual void openEditSignScreen(BlockEntitySign sign)
        {
        }

        public void interact(Entity entity)
        {
            if (!entity.interact(this))
            {
                ItemStack var2 = getHand();
                if (var2 != null && entity is EntityLiving)
                {
                    var2.useOnEntity((EntityLiving)entity);
                    if (var2.count <= 0)
                    {
                        var2.onRemoved(this);
                        clearStackInHand();
                    }
                }

            }
        }

        public ItemStack getHand()
        {
            return inventory.getCurrentItem();
        }

        public void clearStackInHand()
        {
            inventory.setStack(inventory.currentItem, (ItemStack)null);
        }

        public override double getYOffset()
        {
            return (double)(yOffset - 0.5F);
        }

        public virtual void swingHand()
        {
            swingProgressInt = -1;
            isSwinging = true;
        }

        public void attack(Entity target)
        {
            int var2 = inventory.getDamageVsEntity(target);
            if (var2 > 0)
            {
                if (motionY < 0.0D)
                {
                    ++var2;
                }

                target.damage(this, var2);
                ItemStack var3 = getHand();
                if (var3 != null && target is EntityLiving)
                {
                    var3.postHit((EntityLiving)target, this);
                    if (var3.count <= 0)
                    {
                        var3.onRemoved(this);
                        clearStackInHand();
                    }
                }

                if (target is EntityLiving)
                {
                    if (target.isEntityAlive())
                    {
                        commandWolvesToAttack((EntityLiving)target, true);
                    }

                    increaseStat(Stats.Stats.damageDealtStat, var2);
                }
            }

        }

        public virtual void respawn()
        {
        }

        public abstract void spawn();

        public void onCursorStackChanged(ItemStack stack)
        {
        }

        public override void markDead()
        {
            base.markDead();
            inventorySlots.onClosed(this);
            if (craftingInventory != null)
            {
                craftingInventory.onClosed(this);
            }

        }

        public override bool isInsideWall()
        {
            return !sleeping && base.isInsideWall();
        }

        public EnumStatus trySleep(int x, int y, int z)
        {
            if (!worldObj.isRemote)
            {
                if (isSleeping() || !isEntityAlive())
                {
                    return EnumStatus.OTHER_PROBLEM;
                }

                if (worldObj.dimension.isNether)
                {
                    return EnumStatus.NOT_POSSIBLE_HERE;
                }

                if (worldObj.isDaytime())
                {
                    return EnumStatus.NOT_POSSIBLE_NOW;
                }

                if (java.lang.Math.abs(posX - (double)x) > 3.0D || java.lang.Math.abs(posY - (double)y) > 2.0D || java.lang.Math.abs(posZ - (double)z) > 3.0D)
                {
                    return EnumStatus.TOO_FAR_AWAY;
                }
            }

            setBoundingBoxSpacing(0.2F, 0.2F);
            yOffset = 0.2F;
            if (worldObj.blockExists(x, y, z))
            {
                int var4 = worldObj.getBlockMeta(x, y, z);
                int var5 = BlockBed.getDirection(var4);
                float var6 = 0.5F;
                float var7 = 0.5F;
                switch (var5)
                {
                    case 0:
                        var7 = 0.9F;
                        break;
                    case 1:
                        var6 = 0.1F;
                        break;
                    case 2:
                        var7 = 0.1F;
                        break;
                    case 3:
                        var6 = 0.9F;
                        break;
                }

                calculateSleepOffset(var5);
                setPosition((double)((float)x + var6), (double)((float)y + 15.0F / 16.0F), (double)((float)z + var7));
            }
            else
            {
                setPosition((double)((float)x + 0.5F), (double)((float)y + 15.0F / 16.0F), (double)((float)z + 0.5F));
            }

            sleeping = true;
            sleepTimer = 0;
            sleepingPos = new Vec3i(x, y, z);
            motionX = motionZ = motionY = 0.0D;
            if (!worldObj.isRemote)
            {
                worldObj.updateAllPlayersSleepingFlag();
            }

            return EnumStatus.OK;
        }

        private void calculateSleepOffset(int bedDir)
        {
            sleepOffsetX = 0.0F;
            sleepOffsetZ = 0.0F;
            switch (bedDir)
            {
                case 0:
                    sleepOffsetZ = -1.8F;
                    break;
                case 1:
                    sleepOffsetX = 1.8F;
                    break;
                case 2:
                    sleepOffsetZ = 1.8F;
                    break;
                case 3:
                    sleepOffsetX = -1.8F;
                    break;
            }

        }

        public void wakeUp(bool resetSleepTimer, bool updateSleepingPlayers, bool setSpawnPos)
        {
            setBoundingBoxSpacing(0.6F, 1.8F);
            resetEyeHeight();
            Vec3i var4 = sleepingPos;
            Vec3i var5 = sleepingPos;
            if (var4 != null && worldObj.getBlockId(var4.x, var4.y, var4.z) == Block.BED.id)
            {
                BlockBed.updateState(worldObj, var4.x, var4.y, var4.z, false);
                var5 = BlockBed.findWakeUpPosition(worldObj, var4.x, var4.y, var4.z, 0);
                if (var5 == null)
                {
                    var5 = new Vec3i(var4.x, var4.y + 1, var4.z);
                }

                setPosition((double)((float)var5.x + 0.5F), (double)((float)var5.y + yOffset + 0.1F), (double)((float)var5.z + 0.5F));
            }

            sleeping = false;
            if (!worldObj.isRemote && updateSleepingPlayers)
            {
                worldObj.updateAllPlayersSleepingFlag();
            }

            if (resetSleepTimer)
            {
                sleepTimer = 0;
            }
            else
            {
                sleepTimer = 100;
            }

            if (setSpawnPos)
            {
                this.setSpawnPos(sleepingPos);
            }

        }

        private bool isSleepingInBed()
        {
            return worldObj.getBlockId(sleepingPos.x, sleepingPos.y, sleepingPos.z) == Block.BED.id;
        }

        public static Vec3i findRespawnPosition(World world, Vec3i spawnPos)
        {
            ChunkSource var2 = world.getChunkSource();
            var2.loadChunk(spawnPos.x - 3 >> 4, spawnPos.z - 3 >> 4);
            var2.loadChunk(spawnPos.x + 3 >> 4, spawnPos.z - 3 >> 4);
            var2.loadChunk(spawnPos.x - 3 >> 4, spawnPos.z + 3 >> 4);
            var2.loadChunk(spawnPos.x + 3 >> 4, spawnPos.z + 3 >> 4);
            if (world.getBlockId(spawnPos.x, spawnPos.y, spawnPos.z) != Block.BED.id)
            {
                return null;
            }
            else
            {
                Vec3i var3 = BlockBed.findWakeUpPosition(world, spawnPos.x, spawnPos.y, spawnPos.z, 0);
                return var3;
            }
        }

        public float getSleepingRotation()
        {
            if (sleepingPos != null)
            {
                int var1 = worldObj.getBlockMeta(sleepingPos.x, sleepingPos.y, sleepingPos.z);
                int var2 = BlockBed.getDirection(var1);
                switch (var2)
                {
                    case 0:
                        return 90.0F;
                    case 1:
                        return 0.0F;
                    case 2:
                        return 270.0F;
                    case 3:
                        return 180.0F;
                }
            }

            return 0.0F;
        }

        public override bool isSleeping()
        {
            return sleeping;
        }

        public bool isPlayerFullyAsleep()
        {
            return sleeping && sleepTimer >= 100;
        }

        public int getSleepTimer()
        {
            return sleepTimer;
        }

        public virtual void sendMessage(string msg)
        {
        }

        public Vec3i getSpawnPos()
        {
            return playerSpawnCoordinate;
        }

        public void setSpawnPos(Vec3i spawnPos)
        {
            if (spawnPos != null)
            {
                playerSpawnCoordinate = new Vec3i(spawnPos);
            }
            else
            {
                playerSpawnCoordinate = null;
            }

        }

        public void incrementStat(StatBase stat)
        {
            increaseStat(stat, 1);
        }

        public virtual void increaseStat(StatBase stat, int amount)
        {
        }

        protected override void jump()
        {
            base.jump();
            increaseStat(Stats.Stats.jumpStat, 1);
        }

        public override void travel(float x, float z)
        {
            double var3 = posX;
            double var5 = posY;
            double var7 = posZ;
            base.travel(x, z);
            updateMovementStat(posX - var3, posY - var5, posZ - var7);
        }

        private void updateMovementStat(double x, double y, double z)
        {
            if (ridingEntity == null)
            {
                int var7;
                if (isInsideOfMaterial(Material.WATER))
                {
                    var7 = java.lang.Math.round(MathHelper.sqrt_double(x * x + y * y + z * z) * 100.0F);
                    if (var7 > 0)
                    {
                        increaseStat(Stats.Stats.distanceDoveStat, var7);
                    }
                }
                else if (isInWater())
                {
                    var7 = java.lang.Math.round(MathHelper.sqrt_double(x * x + z * z) * 100.0F);
                    if (var7 > 0)
                    {
                        increaseStat(Stats.Stats.distanceSwumStat, var7);
                    }
                }
                else if (isOnLadder())
                {
                    if (y > 0.0D)
                    {
                        increaseStat(Stats.Stats.distanceClimbedStat, (int)java.lang.Math.round(y * 100.0D));
                    }
                }
                else if (onGround)
                {
                    var7 = java.lang.Math.round(MathHelper.sqrt_double(x * x + z * z) * 100.0F);
                    if (var7 > 0)
                    {
                        increaseStat(Stats.Stats.distanceWalkedStat, var7);
                    }
                }
                else
                {
                    var7 = java.lang.Math.round(MathHelper.sqrt_double(x * x + z * z) * 100.0F);
                    if (var7 > 25)
                    {
                        increaseStat(Stats.Stats.distanceFlownStat, var7);
                    }
                }

            }
        }

        private void increaseRidingMotionStats(double x, double y, double z)
        {
            if (ridingEntity != null)
            {
                int var7 = java.lang.Math.round(MathHelper.sqrt_double(x * x + y * y + z * z) * 100.0F);
                if (var7 > 0)
                {
                    if (ridingEntity is EntityMinecart)
                    {
                        increaseStat(Stats.Stats.distanceByMinecartStat, var7);
                        if (startMinecartRidingCoordinate == null)
                        {
                            startMinecartRidingCoordinate = new Vec3i(MathHelper.floor_double(posX), MathHelper.floor_double(posY), MathHelper.floor_double(posZ));
                        }
                        else if (startMinecartRidingCoordinate.getSqDistanceTo(MathHelper.floor_double(posX), MathHelper.floor_double(posY), MathHelper.floor_double(posZ)) >= 1000.0D)
                        {
                            increaseStat(Achievements.CRAFT_RAIL, 1);
                        }
                    }
                    else if (ridingEntity is EntityBoat)
                    {
                        increaseStat(Stats.Stats.distanceByBoatStat, var7);
                    }
                    else if (ridingEntity is EntityPig)
                    {
                        increaseStat(Stats.Stats.distanceByPigStat, var7);
                    }
                }
            }

        }

        protected override void onLanding(float fallDistance)
        {
            if (fallDistance >= 2.0F)
            {
                increaseStat(Stats.Stats.distanceFallenStat, (int)java.lang.Math.round((double)fallDistance * 100.0D));
            }

            base.onLanding(fallDistance);
        }

        public override void onKillOther(EntityLiving other)
        {
            if (other is EntityMob)
            {
                incrementStat(Achievements.KILL_ENEMY);
            }

        }

        public override int getItemStackTextureId(ItemStack stack)
        {
            int var2 = base.getItemStackTextureId(stack);
            if (stack.itemID == Item.FISHING_ROD.id && fishEntity != null)
            {
                var2 = stack.getTextureId() + 16;
            }

            return var2;
        }

        public override void tickPortalCooldown()
        {
            if (timeUntilPortal > 0)
            {
                timeUntilPortal = 10;
            }
            else
            {
                inPortal = true;
            }
        }
    }

}