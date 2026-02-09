using betareborn.Blocks.Entities;
using betareborn.Client.Resource.Language;
using betareborn.Inventorys;
using betareborn.Items;
using betareborn.Network.Packets;
using betareborn.Network.Packets.Play;
using betareborn.Network.Packets.S2CPlay;
using betareborn.Screens;
using betareborn.Screens.Slots;
using betareborn.Server.Network;
using betareborn.Stats;
using betareborn.Util.Maths;
using betareborn.Worlds;
using java.util;

namespace betareborn.Entities
{
    public class ServerPlayerEntity : EntityPlayer, ScreenHandlerListener
    {
        public ServerPlayNetworkHandler networkHandler;
        public MinecraftServer server;
        public ServerPlayerInteractionManager interactionManager;
        public double lastX;
        public double lastZ;
        public List pendingChunkUpdates = new LinkedList();
        public Set activeChunks = new HashSet();
        private int lastHealthScore = -99999999;
        private int joinInvulnerabilityTicks = 60;
        private ItemStack[] equipment = new ItemStack[] { null, null, null, null, null };
        private int screenHandlerSyncId = 0;
        public bool skipPacketSlotUpdates;

        public ServerPlayerEntity(MinecraftServer server, World world, String name, ServerPlayerInteractionManager interactionManager) : base(world)
        {
            interactionManager.player = this;
            this.interactionManager = interactionManager;
            Vec3i var5 = world.getSpawnPos();
            int var6 = var5.x;
            int var7 = var5.z;
            int var8 = var5.y;
            if (!world.dimension.hasCeiling)
            {
                var6 += this.random.nextInt(20) - 10;
                var8 = world.getSpawnPositionValidityY(var6, var7);
                var7 += this.random.nextInt(20) - 10;
            }

            this.setPositionAndAnglesKeepPrevAngles(var6 + 0.5, var8, var7 + 0.5, 0.0F, 0.0F);
            this.server = server;
            this.stepHeight = 0.0F;
            this.name = name;
            this.standingEyeHeight = 0.0F;
        }


        public override void setWorld(World world)
        {
            base.setWorld(world);
            this.interactionManager = new ServerPlayerInteractionManager((ServerWorld)world);
            this.interactionManager.player = this;
        }

        public void initScreenHandler()
        {
            currentScreenHandler.addListener(this);
        }


        public override ItemStack[] getEquipment()
        {
            return this.equipment;
        }


        protected override void resetEyeHeight()
        {
            this.standingEyeHeight = 0.0F;
        }


        public override float getEyeHeight()
        {
            return 1.62F;
        }


        public override void tick()
        {
            this.interactionManager.update();
            this.joinInvulnerabilityTicks--;
            this.currentScreenHandler.sendContentUpdates();

            for (int var1 = 0; var1 < 5; var1++)
            {
                ItemStack var2 = this.getEquipment(var1);
                if (var2 != this.equipment[var1])
                {
                    this.server.getEntityTracker(this.dimensionId).sendToListeners(this, new EntityEquipmentUpdateS2CPacket(this.id, var1, var2));
                    this.equipment[var1] = var2;
                }
            }
        }

        public ItemStack getEquipment(int slot)
        {
            return slot == 0 ? this.inventory.getSelectedItem() : this.inventory.armor[slot - 1];
        }


        public override void onKilledBy(Entity adversary)
        {
            this.inventory.dropInventory();
        }


        public override bool damage(Entity damageSource, int amount)
        {
            if (this.joinInvulnerabilityTicks > 0)
            {
                return false;
            }
            else
            {
                if (!this.server.pvpEnabled)
                {
                    if (damageSource is EntityPlayer)
                    {
                        return false;
                    }

                    if (damageSource is EntityArrow var3)
                    {
                        if (var3.owner is EntityPlayer)
                        {
                            return false;
                        }
                    }
                }

                return base.damage(damageSource, amount);
            }
        }


        protected override bool isPvpEnabled()
        {
            return this.server.pvpEnabled;
        }


        public override void heal(int amount)
        {
            base.heal(amount);
        }

        public void playerTick(bool shouldSendChunkUpdates)
        {
            base.tick();

            for (int var2 = 0; var2 < this.inventory.size(); var2++)
            {
                ItemStack var3 = this.inventory.getStack(var2);
                if (var3 != null && Item.ITEMS[var3.itemId].isNetworkSynced() && this.networkHandler.getBlockDataSendQueueSize() <= 2)
                {
                    Packet var4 = ((NetworkSyncedItem)Item.ITEMS[var3.itemId]).getUpdatePacket(var3, this.world, this);
                    if (var4 != null)
                    {
                        this.networkHandler.sendPacket(var4);
                    }
                }
            }

            if (shouldSendChunkUpdates && !this.pendingChunkUpdates.isEmpty())
            {
                ChunkPos? var7 = (ChunkPos?)this.pendingChunkUpdates.get(0);
                if (var7 != null)
                {
                    bool var8 = false;
                    if (this.networkHandler.getBlockDataSendQueueSize() < 4)
                    {
                        var8 = true;
                    }

                    if (var8)
                    {
                        ServerWorld var9 = this.server.getWorld(this.dimensionId);
                        this.pendingChunkUpdates.remove(var7);
                        this.networkHandler.sendPacket(new ChunkDataS2CPacket(var7.Value.x * 16, 0, var7.Value.z * 16, 16, 128, 16, var9));
                        List var5 = var9.getBlockEntities(var7.Value.x * 16, 0, var7.Value.z * 16, var7.Value.x * 16 + 16, 128, var7.Value.z * 16 + 16);

                        for (int var6 = 0; var6 < var5.size(); var6++)
                        {
                            this.updateBlockEntity((BlockEntity)var5.get(var6));
                        }
                    }
                }
            }

            if (this.inTeleportationState)
            {
                if (this.server.properties.getProperty("allow-nether", true))
                {
                    if (this.currentScreenHandler != this.playerScreenHandler)
                    {
                        this.closeHandledScreen();
                    }

                    if (this.vehicle != null)
                    {
                        this.setVehicle(this.vehicle);
                    }
                    else
                    {
                        this.changeDimensionCooldown += 0.0125F;
                        if (this.changeDimensionCooldown >= 1.0F)
                        {
                            this.changeDimensionCooldown = 1.0F;
                            this.portalCooldown = 10;
                            this.server.playerManager.changePlayerDimension(this);
                        }
                    }

                    this.inTeleportationState = false;
                }
            }
            else
            {
                if (this.changeDimensionCooldown > 0.0F)
                {
                    this.changeDimensionCooldown -= 0.05F;
                }

                if (this.changeDimensionCooldown < 0.0F)
                {
                    this.changeDimensionCooldown = 0.0F;
                }
            }

            if (this.portalCooldown > 0)
            {
                this.portalCooldown--;
            }

            if (this.health != this.lastHealthScore)
            {
                this.networkHandler.sendPacket(new HealthUpdateS2CPacket(this.health));
                this.lastHealthScore = this.health;
            }
        }

        private void updateBlockEntity(BlockEntity blockentity)
        {
            if (blockentity != null)
            {
                Packet var2 = blockentity.createUpdatePacket();
                if (var2 != null)
                {
                    this.networkHandler.sendPacket(var2);
                }
            }
        }


        public override void tickMovement()
        {
            base.tickMovement();
        }


        public override void sendPickup(Entity item, int count)
        {
            if (!item.dead)
            {
                EntityTracker var3 = this.server.getEntityTracker(this.dimensionId);
                if (item is EntityItem)
                {
                    var3.sendToListeners(item, new ItemPickupAnimationS2CPacket(item.id, this.id));
                }

                if (item is EntityArrow)
                {
                    var3.sendToListeners(item, new ItemPickupAnimationS2CPacket(item.id, this.id));
                }
            }

            base.sendPickup(item, count);
            this.currentScreenHandler.sendContentUpdates();
        }


        public override void swingHand()
        {
            if (!this.handSwinging)
            {
                this.handSwingTicks = -1;
                this.handSwinging = true;
                EntityTracker var1 = this.server.getEntityTracker(this.dimensionId);
                var1.sendToListeners(this, new EntityAnimationPacket(this, 1));
            }
        }

        public void m_41544513()
        {
        }


        public override SleepAttemptResult trySleep(int x, int y, int z)
        {
            SleepAttemptResult var4 = base.trySleep(x, y, z);
            if (var4 == SleepAttemptResult.OK)
            {
                EntityTracker var5 = this.server.getEntityTracker(this.dimensionId);
                PlayerSleepUpdateS2CPacket var6 = new PlayerSleepUpdateS2CPacket(this, 0, x, y, z);
                var5.sendToListeners(this, var6);
                this.networkHandler.teleport(this.x, this.y, this.z, this.yaw, this.pitch);
                this.networkHandler.sendPacket(var6);
            }

            return var4;
        }


        public override void wakeUp(bool resetSleepTimer, bool updateSleepingPlayers, bool setSpawnPos)
        {
            if (this.isSleeping())
            {
                EntityTracker var4 = this.server.getEntityTracker(this.dimensionId);
                var4.sendToAround(this, new EntityAnimationPacket(this, 3));
            }

            base.wakeUp(resetSleepTimer, updateSleepingPlayers, setSpawnPos);
            if (this.networkHandler != null)
            {
                this.networkHandler.teleport(this.x, this.y, this.z, this.yaw, this.pitch);
            }
        }


        public override void setVehicle(Entity entity)
        {
            base.setVehicle(entity);
            this.networkHandler.sendPacket(new EntityVehicleSetS2CPacket(this, this.vehicle));
            this.networkHandler.teleport(this.x, this.y, this.z, this.yaw, this.pitch);
        }


        protected override void fall(double heightDifference, bool onGround)
        {
        }

        public void handleFall(double heightDifference, bool onGround)
        {
            base.fall(heightDifference, onGround);
        }

        private void incrementScreenHandlerSyncId()
        {
            this.screenHandlerSyncId = this.screenHandlerSyncId % 100 + 1;
        }


        public override void openCraftingScreen(int x, int y, int z)
        {
            this.incrementScreenHandlerSyncId();
            this.networkHandler.sendPacket(new OpenScreenS2CPacket(this.screenHandlerSyncId, 1, "Crafting", 9));
            this.currentScreenHandler = new CraftingScreenHandler(this.inventory, this.world, x, y, z);
            this.currentScreenHandler.syncId = this.screenHandlerSyncId;
            this.currentScreenHandler.addListener(this);
        }


        public override void openChestScreen(IInventory inventory)
        {
            this.incrementScreenHandlerSyncId();
            this.networkHandler.sendPacket(new OpenScreenS2CPacket(this.screenHandlerSyncId, 0, inventory.getName(), inventory.size()));
            this.currentScreenHandler = new GenericContainerScreenHandler(this.inventory, inventory);
            this.currentScreenHandler.syncId = this.screenHandlerSyncId;
            this.currentScreenHandler.addListener(this);
        }


        public override void openFurnaceScreen(BlockEntityFurnace furnace)
        {
            this.incrementScreenHandlerSyncId();
            this.networkHandler.sendPacket(new OpenScreenS2CPacket(this.screenHandlerSyncId, 2, furnace.getName(), furnace.size()));
            this.currentScreenHandler = new FurnaceScreenHandler(this.inventory, furnace);
            this.currentScreenHandler.syncId = this.screenHandlerSyncId;
            this.currentScreenHandler.addListener(this);
        }


        public override void openDispenserScreen(BlockEntityDispenser dispenser)
        {
            this.incrementScreenHandlerSyncId();
            this.networkHandler.sendPacket(new OpenScreenS2CPacket(this.screenHandlerSyncId, 3, dispenser.getName(), dispenser.size()));
            this.currentScreenHandler = new DispenserScreenHandler(this.inventory, dispenser);
            this.currentScreenHandler.syncId = this.screenHandlerSyncId;
            this.currentScreenHandler.addListener(this);
        }


        public void onSlotUpdate(ScreenHandler handler, int slot, ItemStack stack)
        {
            if (!(handler.getSlot(slot) is CraftingResultSlot))
            {
                if (!this.skipPacketSlotUpdates)
                {
                    this.networkHandler.sendPacket(new ScreenHandlerSlotUpdateS2CPacket(handler.syncId, slot, stack));
                }
            }
        }

        public void onContentsUpdate(ScreenHandler screenHandler)
        {
            this.onContentsUpdate(screenHandler, screenHandler.getStacks());
        }


        public void onContentsUpdate(ScreenHandler handler, List stacks)
        {
            this.networkHandler.sendPacket(new InventoryS2CPacket(handler.syncId, stacks));
            this.networkHandler.sendPacket(new ScreenHandlerSlotUpdateS2CPacket(-1, -1, this.inventory.getCursorStack()));
        }


        public void onPropertyUpdate(ScreenHandler handler, int syncId, int trackedValue)
        {
            this.networkHandler.sendPacket(new ScreenHandlerPropertyUpdateS2CPacket(handler.syncId, syncId, trackedValue));
        }


        public override void onCursorStackChanged(ItemStack stack)
        {
        }


        public override void closeHandledScreen()
        {
            networkHandler.sendPacket(new CloseScreenS2CPacket(currentScreenHandler.syncId));
            onHandledScreenClosed();
        }

        public void updateCursorStack()
        {
            if (!skipPacketSlotUpdates)
            {
                networkHandler.sendPacket(new ScreenHandlerSlotUpdateS2CPacket(-1, -1, inventory.getCursorStack()));
            }
        }

        public void onHandledScreenClosed()
        {
            currentScreenHandler.onClosed(this);
            currentScreenHandler = playerScreenHandler;
        }

        public void updateInput(float sidewaysSpeed, float forwardSpeed, bool jumping, bool sneaking, float pitch, float yaw)
        {
            this.sidewaysSpeed = sidewaysSpeed;
            this.forwardSpeed = forwardSpeed;
            this.jumping = jumping;
            setSneaking(sneaking);
            this.pitch = pitch;
            this.yaw = yaw;
        }


        public override void increaseStat(StatBase stat, int amount)
        {
            if (stat != null)
            {
                if (!stat.localOnly)
                {
                    while (amount > 100)
                    {
                        this.networkHandler.sendPacket(new IncreaseStatS2CPacket(stat.id, 100));
                        amount -= 100;
                    }

                    this.networkHandler.sendPacket(new IncreaseStatS2CPacket(stat.id, amount));
                }
            }
        }

        public void onDisconnect()
        {
            if (this.vehicle != null)
            {
                this.setVehicle(this.vehicle);
            }

            if (this.passenger != null)
            {
                this.passenger.setVehicle(this);
            }

            if (this.sleeping)
            {
                this.wakeUp(true, false, false);
            }
        }

        public void markHealthDirty()
        {
            this.lastHealthScore = -99999999;
        }


        public override void sendMessage(string message)
        {
            TranslationStorage var2 = TranslationStorage.getInstance();
            string var3 = var2.translateKey(message);
            this.networkHandler.sendPacket(new ChatMessagePacket(var3));
        }
    }
}
