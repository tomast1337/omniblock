using betareborn.Blocks.Entities;
using betareborn.Client.Resource.Language;
using betareborn.Inventorys;
using betareborn.Items;
using betareborn.Network.Packets;
using betareborn.Network.Packets.Play;
using betareborn.Network.Packets.S2CPlay;
using betareborn.Screens;
using betareborn.Screens.Slots;
using betareborn.Server;
using betareborn.Server.Entities;
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
        public HashSet<ChunkPos> activeChunks = new HashSet<ChunkPos>();
        private int lastHealthScore = -99999999;
        private int joinInvulnerabilityTicks = 60;
        private ItemStack[] equipment = [null, null, null, null, null];
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
                var6 += random.nextInt(20) - 10;
                var8 = world.getSpawnPositionValidityY(var6, var7);
                var7 += random.nextInt(20) - 10;
            }

            setPositionAndAnglesKeepPrevAngles(var6 + 0.5, var8, var7 + 0.5, 0.0F, 0.0F);
            this.server = server;
            stepHeight = 0.0F;
            this.name = name;
            standingEyeHeight = 0.0F;
        }


        public override void setWorld(World world)
        {
            base.setWorld(world);
            interactionManager = new ServerPlayerInteractionManager((ServerWorld)world);
            interactionManager.player = this;
        }

        public void initScreenHandler()
        {
            currentScreenHandler.addListener(this);
        }


        public override ItemStack[] getEquipment()
        {
            return equipment;
        }


        protected override void resetEyeHeight()
        {
            standingEyeHeight = 0.0F;
        }


        public override float getEyeHeight()
        {
            return 1.62F;
        }


        public override void tick()
        {
            interactionManager.update();
            joinInvulnerabilityTicks--;
            currentScreenHandler.sendContentUpdates();

            for (int var1 = 0; var1 < 5; var1++)
            {
                ItemStack var2 = getEquipment(var1);
                if (var2 != equipment[var1])
                {
                    server.getEntityTracker(dimensionId).sendToListeners(this, new EntityEquipmentUpdateS2CPacket(id, var1, var2));
                    equipment[var1] = var2;
                }
            }
        }

        public ItemStack getEquipment(int slot)
        {
            return slot == 0 ? inventory.getSelectedItem() : inventory.armor[slot - 1];
        }


        public override void onKilledBy(Entity adversary)
        {
            inventory.dropInventory();
        }


        public override bool damage(Entity damageSource, int amount)
        {
            if (joinInvulnerabilityTicks > 0)
            {
                return false;
            }
            else
            {
                if (!server.pvpEnabled)
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
            return server.pvpEnabled;
        }


        public override void heal(int amount)
        {
            base.heal(amount);
        }

        public void playerTick(bool shouldSendChunkUpdates)
        {
            base.tick();

            for (int var2 = 0; var2 < inventory.size(); var2++)
            {
                ItemStack var3 = inventory.getStack(var2);
                if (var3 != null && Item.ITEMS[var3.itemId].isNetworkSynced() && networkHandler.getBlockDataSendQueueSize() <= 2)
                {
                    Packet var4 = ((NetworkSyncedItem)Item.ITEMS[var3.itemId]).getUpdatePacket(var3, world, this);
                    if (var4 != null)
                    {
                        networkHandler.sendPacket(var4);
                    }
                }
            }

            if (shouldSendChunkUpdates && !pendingChunkUpdates.isEmpty())
            {
                ChunkPos? var7 = (ChunkPos?)pendingChunkUpdates.get(0);
                if (var7 != null)
                {
                    bool var8 = false;
                    if (networkHandler.getBlockDataSendQueueSize() < 4)
                    {
                        var8 = true;
                    }

                    if (var8)
                    {
                        ServerWorld var9 = server.getWorld(dimensionId);
                        pendingChunkUpdates.remove(var7);
                        networkHandler.sendPacket(new ChunkDataS2CPacket(var7.Value.x * 16, 0, var7.Value.z * 16, 16, 128, 16, var9));
                        var var5 = var9.getBlockEntities(var7.Value.x * 16, 0, var7.Value.z * 16, var7.Value.x * 16 + 16, 128, var7.Value.z * 16 + 16);

                        for (int var6 = 0; var6 < var5.Count; var6++)
                        {
                            updateBlockEntity(var5[var6]);
                        }
                    }
                }
            }

            if (inTeleportationState)
            {
                if (server.config.GetAllowNether(true))
                {
                    if (currentScreenHandler != playerScreenHandler)
                    {
                        closeHandledScreen();
                    }

                    if (vehicle != null)
                    {
                        setVehicle(vehicle);
                    }
                    else
                    {
                        changeDimensionCooldown += 0.0125F;
                        if (changeDimensionCooldown >= 1.0F)
                        {
                            changeDimensionCooldown = 1.0F;
                            portalCooldown = 10;
                            server.playerManager.changePlayerDimension(this);
                        }
                    }

                    inTeleportationState = false;
                }
            }
            else
            {
                if (changeDimensionCooldown > 0.0F)
                {
                    changeDimensionCooldown -= 0.05F;
                }

                if (changeDimensionCooldown < 0.0F)
                {
                    changeDimensionCooldown = 0.0F;
                }
            }

            if (portalCooldown > 0)
            {
                portalCooldown--;
            }

            if (health != lastHealthScore)
            {
                networkHandler.sendPacket(new HealthUpdateS2CPacket(health));
                lastHealthScore = health;
            }
        }

        private void updateBlockEntity(BlockEntity blockentity)
        {
            if (blockentity != null)
            {
                Packet var2 = blockentity.createUpdatePacket();
                if (var2 != null)
                {
                    networkHandler.sendPacket(var2);
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
                EntityTracker var3 = server.getEntityTracker(dimensionId);
                if (item is EntityItem)
                {
                    var3.sendToListeners(item, new ItemPickupAnimationS2CPacket(item.id, id));
                }

                if (item is EntityArrow)
                {
                    var3.sendToListeners(item, new ItemPickupAnimationS2CPacket(item.id, id));
                }
            }

            base.sendPickup(item, count);
            currentScreenHandler.sendContentUpdates();
        }


        public override void swingHand()
        {
            if (!handSwinging)
            {
                handSwingTicks = -1;
                handSwinging = true;
                EntityTracker var1 = server.getEntityTracker(dimensionId);
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
                EntityTracker var5 = server.getEntityTracker(dimensionId);
                PlayerSleepUpdateS2CPacket var6 = new PlayerSleepUpdateS2CPacket(this, 0, x, y, z);
                var5.sendToListeners(this, var6);
                networkHandler.teleport(x, y, z, yaw, pitch);
                networkHandler.sendPacket(var6);
            }

            return var4;
        }


        public override void wakeUp(bool resetSleepTimer, bool updateSleepingPlayers, bool setSpawnPos)
        {
            if (isSleeping())
            {
                EntityTracker var4 = server.getEntityTracker(dimensionId);
                var4.sendToAround(this, new EntityAnimationPacket(this, 3));
            }

            base.wakeUp(resetSleepTimer, updateSleepingPlayers, setSpawnPos);
            if (networkHandler != null)
            {
                networkHandler.teleport(x, y, z, yaw, pitch);
            }
        }


        public override void setVehicle(Entity entity)
        {
            base.setVehicle(entity);
            networkHandler.sendPacket(new EntityVehicleSetS2CPacket(this, vehicle));
            networkHandler.teleport(x, y, z, yaw, pitch);
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
            screenHandlerSyncId = screenHandlerSyncId % 100 + 1;
        }


        public override void openCraftingScreen(int x, int y, int z)
        {
            incrementScreenHandlerSyncId();
            networkHandler.sendPacket(new OpenScreenS2CPacket(screenHandlerSyncId, 1, "Crafting", 9));
            currentScreenHandler = new CraftingScreenHandler(inventory, world, x, y, z);
            currentScreenHandler.syncId = screenHandlerSyncId;
            currentScreenHandler.addListener(this);
        }


        public override void openChestScreen(IInventory inventory)
        {
            incrementScreenHandlerSyncId();
            networkHandler.sendPacket(new OpenScreenS2CPacket(screenHandlerSyncId, 0, inventory.getName(), inventory.size()));
            currentScreenHandler = new GenericContainerScreenHandler(inventory, inventory);
            currentScreenHandler.syncId = screenHandlerSyncId;
            currentScreenHandler.addListener(this);
        }


        public override void openFurnaceScreen(BlockEntityFurnace furnace)
        {
            incrementScreenHandlerSyncId();
            networkHandler.sendPacket(new OpenScreenS2CPacket(screenHandlerSyncId, 2, furnace.getName(), furnace.size()));
            currentScreenHandler = new FurnaceScreenHandler(inventory, furnace);
            currentScreenHandler.syncId = screenHandlerSyncId;
            currentScreenHandler.addListener(this);
        }


        public override void openDispenserScreen(BlockEntityDispenser dispenser)
        {
            incrementScreenHandlerSyncId();
            networkHandler.sendPacket(new OpenScreenS2CPacket(screenHandlerSyncId, 3, dispenser.getName(), dispenser.size()));
            currentScreenHandler = new DispenserScreenHandler(inventory, dispenser);
            currentScreenHandler.syncId = screenHandlerSyncId;
            currentScreenHandler.addListener(this);
        }


        public void onSlotUpdate(ScreenHandler handler, int slot, ItemStack stack)
        {
            if (!(handler.getSlot(slot) is CraftingResultSlot))
            {
                if (!skipPacketSlotUpdates)
                {
                    networkHandler.sendPacket(new ScreenHandlerSlotUpdateS2CPacket(handler.syncId, slot, stack));
                }
            }
        }

        public void onContentsUpdate(ScreenHandler screenHandler)
        {
            onContentsUpdate(screenHandler, screenHandler.getStacks());
        }


        public void onContentsUpdate(ScreenHandler handler, List stacks)
        {
            networkHandler.sendPacket(new InventoryS2CPacket(handler.syncId, stacks));
            networkHandler.sendPacket(new ScreenHandlerSlotUpdateS2CPacket(-1, -1, inventory.getCursorStack()));
        }


        public void onPropertyUpdate(ScreenHandler handler, int syncId, int trackedValue)
        {
            networkHandler.sendPacket(new ScreenHandlerPropertyUpdateS2CPacket(handler.syncId, syncId, trackedValue));
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
                        networkHandler.sendPacket(new IncreaseStatS2CPacket(stat.id, 100));
                        amount -= 100;
                    }

                    networkHandler.sendPacket(new IncreaseStatS2CPacket(stat.id, amount));
                }
            }
        }

        public void onDisconnect()
        {
            if (vehicle != null)
            {
                setVehicle(vehicle);
            }

            if (passenger != null)
            {
                passenger.setVehicle(this);
            }

            if (sleeping)
            {
                wakeUp(true, false, false);
            }
        }

        public void markHealthDirty()
        {
            lastHealthScore = -99999999;
        }


        public override void sendMessage(string message)
        {
            TranslationStorage var2 = TranslationStorage.getInstance();
            string var3 = var2.translateKey(message);
            networkHandler.sendPacket(new ChatMessagePacket(var3));
        }

        public override void spawn()
        {
            //client only
            throw new NotImplementedException();
        }
    }
}
