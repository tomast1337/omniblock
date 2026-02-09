using betareborn.Blocks;
using betareborn.Blocks.Entities;
using betareborn.Client.Guis;
using betareborn.Entities;
using betareborn.Inventorys;
using betareborn.Items;
using betareborn.Network;
using betareborn.Network.Packets;
using betareborn.Network.Packets.Play;
using betareborn.Network.Packets.S2CPlay;
using betareborn.Screens;
using betareborn.Util.Maths;
using betareborn.Worlds;
using betareborn.Worlds.Chunks;
using betareborn.Worlds.Storage;
using java.io;
using java.net;

namespace betareborn.Client.Network
{
    public class ClientNetworkHandler : NetHandler
    {
        private bool disconnected = false;
        private Connection netManager;
        public string field_1209_a;
        private Minecraft mc;
        private ClientWorld worldClient;
        private bool field_1210_g = false;
        public PersistentStateManager clientPersistentStateManager = new PersistentStateManager(null);
        java.util.Random rand = new();

        public ClientNetworkHandler(Minecraft var1, string var2, int var3)
        {

            mc = var1;
            Socket socket = new Socket(InetAddress.getByName(var2), var3);
            socket.setTcpNoDelay(true);
            netManager = new Connection(socket, "Client", this);
        }

        public void tick()
        {
            if (!disconnected)
            {
                netManager.tick();
            }

            netManager.interrupt();
        }

        public override void handleLogin(LoginHelloPacket var1)
        {
            mc.playerController = new PlayerControllerMP(mc, this);
            mc.statFileWriter.readStat(Stats.Stats.joinMultiplayerStat, 1);
            worldClient = new ClientWorld(this, var1.mapSeed, var1.dimension);
            worldClient.isRemote = true;
            mc.changeWorld1(worldClient);
            mc.player.dimension = var1.dimension;
            mc.displayGuiScreen(new GuiDownloadTerrain(this));
            mc.player.entityId = var1.protocolVersion;
        }

        public override void handlePickupSpawn(ItemEntitySpawnS2CPacket var1)
        {
            double var2 = var1.xPosition / 32.0D;
            double var4 = var1.yPosition / 32.0D;
            double var6 = var1.zPosition / 32.0D;
            EntityItem var8 = new EntityItem(worldClient, var2, var4, var6, new ItemStack(var1.itemID, var1.count, var1.itemDamage));
            var8.velocityX = var1.rotation / 128.0D;
            var8.velocityY = var1.pitch / 128.0D;
            var8.velocityZ = var1.roll / 128.0D;
            var8.trackedPosX = var1.xPosition;
            var8.trackedPosY = var1.yPosition;
            var8.trackedPosZ = var1.zPosition;
            worldClient.forceEntity(var1.entityId, var8);
        }

        public override void handleVehicleSpawn(EntitySpawnS2CPacket var1)
        {
            double var2 = var1.xPosition / 32.0D;
            double var4 = var1.yPosition / 32.0D;
            double var6 = var1.zPosition / 32.0D;
            object var8 = null;
            if (var1.type == 10)
            {
                var8 = new EntityMinecart(worldClient, var2, var4, var6, 0);
            }

            if (var1.type == 11)
            {
                var8 = new EntityMinecart(worldClient, var2, var4, var6, 1);
            }

            if (var1.type == 12)
            {
                var8 = new EntityMinecart(worldClient, var2, var4, var6, 2);
            }

            if (var1.type == 90)
            {
                var8 = new EntityFish(worldClient, var2, var4, var6);
            }

            if (var1.type == 60)
            {
                var8 = new EntityArrow(worldClient, var2, var4, var6);
            }

            if (var1.type == 61)
            {
                var8 = new EntitySnowball(worldClient, var2, var4, var6);
            }

            if (var1.type == 63)
            {
                var8 = new EntityFireball(worldClient, var2, var4, var6, var1.field_28047_e / 8000.0D, var1.field_28046_f / 8000.0D, var1.field_28045_g / 8000.0D);
                var1.field_28044_i = 0;
            }

            if (var1.type == 62)
            {
                var8 = new EntityEgg(worldClient, var2, var4, var6);
            }

            if (var1.type == 1)
            {
                var8 = new EntityBoat(worldClient, var2, var4, var6);
            }

            if (var1.type == 50)
            {
                var8 = new EntityTNTPrimed(worldClient, var2, var4, var6);
            }

            if (var1.type == 70)
            {
                var8 = new EntityFallingSand(worldClient, var2, var4, var6, Block.SAND.id);
            }

            if (var1.type == 71)
            {
                var8 = new EntityFallingSand(worldClient, var2, var4, var6, Block.GRAVEL.id);
            }

            if (var8 != null)
            {
                ((Entity)var8).trackedPosX = var1.xPosition;
                ((Entity)var8).trackedPosY = var1.yPosition;
                ((Entity)var8).trackedPosZ = var1.zPosition;
                ((Entity)var8).yaw = 0.0F;
                ((Entity)var8).pitch = 0.0F;
                ((Entity)var8).entityId = var1.entityId;
                worldClient.forceEntity(var1.entityId, (Entity)var8);
                if (var1.field_28044_i > 0)
                {
                    if (var1.type == 60)
                    {
                        Entity var9 = getEntityByID(var1.field_28044_i);
                        if (var9 is EntityLiving)
                        {
                            ((EntityArrow)var8).owner = (EntityLiving)var9;
                        }
                    }

                    ((Entity)var8).setVelocity(var1.field_28047_e / 8000.0D, var1.field_28046_f / 8000.0D, var1.field_28045_g / 8000.0D);
                }
            }

        }

        public override void handleWeather(GlobalEntitySpawnS2CPacket var1)
        {
            double var2 = var1.field_27053_b / 32.0D;
            double var4 = var1.field_27057_c / 32.0D;
            double var6 = var1.field_27056_d / 32.0D;
            EntityLightningBolt var8 = null;
            if (var1.field_27055_e == 1)
            {
                var8 = new EntityLightningBolt(worldClient, var2, var4, var6);
            }

            if (var8 != null)
            {
                var8.trackedPosX = var1.field_27053_b;
                var8.trackedPosY = var1.field_27057_c;
                var8.trackedPosZ = var1.field_27056_d;
                var8.yaw = 0.0F;
                var8.pitch = 0.0F;
                var8.entityId = var1.field_27054_a;
                worldClient.spawnGlobalEntity(var8);
            }

        }

        public override void func_21146_a(PaintingEntitySpawnS2CPacket var1)
        {
            EntityPainting var2 = new EntityPainting(worldClient, var1.xPosition, var1.yPosition, var1.zPosition, var1.direction, var1.title);
            worldClient.forceEntity(var1.entityId, var2);
        }

        public override void func_6498_a(EntityVelocityUpdateS2CPacket var1)
        {
            Entity var2 = getEntityByID(var1.entityId);
            if (var2 != null)
            {
                var2.setVelocity(var1.motionX / 8000.0D, var1.motionY / 8000.0D, var1.motionZ / 8000.0D);
            }
        }

        public override void func_21148_a(EntityTrackerUpdateS2CPacket var1)
        {
            Entity var2 = getEntityByID(var1.entityId);
            if (var2 != null && var1.func_21047_b() != null)
            {
                var2.getDataWatcher().updateWatchedObjectsFromList(var1.func_21047_b());
            }

        }

        public override void handleNamedEntitySpawn(PlayerSpawnS2CPacket var1)
        {
            double var2 = var1.xPosition / 32.0D;
            double var4 = var1.yPosition / 32.0D;
            double var6 = var1.zPosition / 32.0D;
            float var8 = var1.rotation * 360 / 256.0F;
            float var9 = var1.pitch * 360 / 256.0F;
            OtherPlayerEntity var10 = new OtherPlayerEntity(mc.world, var1.name);
            var10.prevX = var10.lastTickX = var10.trackedPosX = var1.xPosition;
            var10.prevY = var10.lastTickY = var10.trackedPosY = var1.yPosition;
            var10.prevZ = var10.lastTickZ = var10.trackedPosZ = var1.zPosition;
            int var11 = var1.currentItem;
            if (var11 == 0)
            {
                var10.inventory.mainInventory[var10.inventory.currentItem] = null;
            }
            else
            {
                var10.inventory.mainInventory[var10.inventory.currentItem] = new ItemStack(var11, 1, 0);
            }

            var10.setPositionAndRotation(var2, var4, var6, var8, var9);
            worldClient.forceEntity(var1.entityId, var10);
        }

        public override void handleEntityTeleport(EntityPositionS2CPacket var1)
        {
            Entity var2 = getEntityByID(var1.entityId);
            if (var2 != null)
            {
                var2.trackedPosX = var1.xPosition;
                var2.trackedPosY = var1.yPosition;
                var2.trackedPosZ = var1.zPosition;
                double var3 = var2.trackedPosX / 32.0D;
                double var5 = var2.trackedPosY / 32.0D + 1.0D / 64.0D;
                double var7 = var2.trackedPosZ / 32.0D;
                float var9 = var1.yaw * 360 / 256.0F;
                float var10 = var1.pitch * 360 / 256.0F;
                var2.setPositionAndRotation2(var3, var5, var7, var9, var10, 3);
            }
        }

        public override void handleEntity(EntityS2CPacket var1)
        {
            Entity var2 = getEntityByID(var1.entityId);
            if (var2 != null)
            {
                var2.trackedPosX += var1.xPosition;
                var2.trackedPosY += var1.yPosition;
                var2.trackedPosZ += var1.zPosition;
                double var3 = var2.trackedPosX / 32.0D;
                double var5 = var2.trackedPosY / 32.0D;
                double var7 = var2.trackedPosZ / 32.0D;
                float var9 = var1.rotating ? var1.yaw * 360 / 256.0F : var2.yaw;
                float var10 = var1.rotating ? var1.pitch * 360 / 256.0F : var2.pitch;
                var2.setPositionAndRotation2(var3, var5, var7, var9, var10, 3);
            }
        }

        public override void handleDestroyEntity(EntityDestroyS2CPacket var1)
        {
            worldClient.removeEntityFromWorld(var1.entityId);
        }

        public override void handleFlying(PlayerMovePacket var1)
        {
            ClientPlayerEntity var2 = mc.player;
            double var3 = var2.x;
            double var5 = var2.y;
            double var7 = var2.z;
            float var9 = var2.yaw;
            float var10 = var2.pitch;
            if (var1.moving)
            {
                var3 = var1.xPosition;
                var5 = var1.yPosition;
                var7 = var1.zPosition;
            }

            if (var1.rotating)
            {
                var9 = var1.yaw;
                var10 = var1.pitch;
            }

            var2.cameraOffset = 0.0F;
            var2.velocityX = var2.velocityY = var2.velocityZ = 0.0D;
            var2.setPositionAndRotation(var3, var5, var7, var9, var10);
            var1.xPosition = var2.x;
            var1.yPosition = var2.boundingBox.minY;
            var1.zPosition = var2.z;
            var1.stance = var2.y;
            netManager.sendPacket(var1);
            if (!field_1210_g)
            {
                mc.player.prevX = mc.player.x;
                mc.player.prevY = mc.player.y;
                mc.player.prevZ = mc.player.z;
                field_1210_g = true;
                mc.displayGuiScreen(null);
            }

        }

        public override void handlePreChunk(ChunkStatusUpdateS2CPacket var1)
        {
            worldClient.updateChunk(var1.xPosition, var1.yPosition, var1.mode);
        }

        public override void handleMultiBlockChange(ChunkDeltaUpdateS2CPacket var1)
        {
            Chunk var2 = worldClient.getChunk(var1.xPosition, var1.zPosition);
            int var3 = var1.xPosition * 16;
            int var4 = var1.zPosition * 16;

            for (int var5 = 0; var5 < var1._size; ++var5)
            {
                short var6 = var1.coordinateArray[var5];
                int var7 = var1.typeArray[var5] & 255;
                byte var8 = var1.metadataArray[var5];
                int var9 = var6 >> 12 & 15;
                int var10 = var6 >> 8 & 15;
                int var11 = var6 & 255;
                var2.setBlock(var9, var11, var10, var7, var8);
                worldClient.clearBlockResets(var9 + var3, var11, var10 + var4, var9 + var3, var11, var10 + var4);
                worldClient.setBlocksDirty(var9 + var3, var11, var10 + var4, var9 + var3, var11, var10 + var4);
            }

        }

        public override void handleMapChunk(ChunkDataS2CPacket var1)
        {
            worldClient.clearBlockResets(var1.xPosition, var1.yPosition, var1.zPosition, var1.xPosition + var1.xSize - 1, var1.yPosition + var1.ySize - 1, var1.zPosition + var1.zSize - 1);
            worldClient.handleChunkDataUpdate(var1.xPosition, var1.yPosition, var1.zPosition, var1.xSize, var1.ySize, var1.zSize, var1.chunk);
        }

        public override void handleBlockChange(BlockUpdateS2CPacket var1)
        {
            worldClient.setBlockWithMetaFromPacket(var1.xPosition, var1.yPosition, var1.zPosition, var1.type, var1.metadata);
        }

        public override void handleKickDisconnect(DisconnectPacket var1)
        {
            netManager.disconnect("disconnect.kicked", new object[0]);
            disconnected = true;
            mc.changeWorld1(null);
            mc.displayGuiScreen(new GuiConnectFailed("disconnect.disconnected", "disconnect.genericReason", new object[] { var1.reason }));
        }

        public override void handleErrorMessage(string var1, object[] var2)
        {
            if (!disconnected)
            {
                disconnected = true;
                mc.changeWorld1(null);
                mc.displayGuiScreen(new GuiConnectFailed("disconnect.lost", var1, var2));
            }
        }

        public void sendPacketAndDisconnect(Packet var1)
        {
            if (!disconnected)
            {
                netManager.sendPacket(var1);
                netManager.disconnect();
            }
        }

        public void addToSendQueue(Packet var1)
        {
            if (!disconnected)
            {
                netManager.sendPacket(var1);
            }
        }

        public override void handleCollect(ItemPickupAnimationS2CPacket var1)
        {
            Entity var2 = getEntityByID(var1.collectedEntityId);
            object var3 = (EntityLiving)getEntityByID(var1.collectorEntityId);
            if (var3 == null)
            {
                var3 = mc.player;
            }

            if (var2 != null)
            {
                worldClient.playSound(var2, "random.pop", 0.2F, ((rand.nextFloat() - rand.nextFloat()) * 0.7F + 1.0F) * 2.0F);
                mc.particleManager.addEffect(new EntityPickupFX(mc.world, var2, (Entity)var3, -0.5F));
                worldClient.removeEntityFromWorld(var1.collectedEntityId);
            }

        }

        public override void handleChat(ChatMessagePacket var1)
        {
            mc.ingameGUI.addChatMessage(var1.message);
        }

        public override void handleArmAnimation(EntityAnimationPacket var1)
        {
            Entity var2 = getEntityByID(var1.entityId);
            if (var2 != null)
            {
                EntityPlayer var3;
                if (var1.animate == 1)
                {
                    var3 = (EntityPlayer)var2;
                    var3.swingHand();
                }
                else if (var1.animate == 2)
                {
                    var2.performHurtAnimation();
                }
                else if (var1.animate == 3)
                {
                    var3 = (EntityPlayer)var2;
                    var3.wakeUp(false, false, false);
                }
                else if (var1.animate == 4)
                {
                    var3 = (EntityPlayer)var2;
                    var3.spawn();
                }

            }
        }

        public override void func_22186_a(PlayerSleepUpdateS2CPacket var1)
        {
            Entity var2 = getEntityByID(var1.field_22045_a);
            if (var2 != null)
            {
                if (var1.field_22046_e == 0)
                {
                    EntityPlayer var3 = (EntityPlayer)var2;
                    var3.trySleep(var1.field_22044_b, var1.field_22048_c, var1.field_22047_d);
                }

            }
        }

        public override void handleHandshake(HandshakePacket var1)
        {
            if (var1.username.Equals("-"))
            {
                addToSendQueue(new LoginHelloPacket(mc.session.username, 14));
            }
            else
            {
                try
                {
                    URL var2 = new URL("http://www.minecraft.net/game/joinserver.jsp?user=" + mc.session.username + "&sessionId=" + mc.session.sessionId + "&serverId=" + var1.username);
                    BufferedReader var3 = new BufferedReader(new InputStreamReader(var2.openStream()));
                    string var4 = var3.readLine();
                    var3.close();
                    //TODO: AUTH
                    if (var4 == null || var4.Equals("ok", StringComparison.OrdinalIgnoreCase))
                    {
                        addToSendQueue(new LoginHelloPacket(mc.session.username, 14));
                    }
                    else
                    {
                        netManager.disconnect("disconnect.loginFailedInfo", new object[] { var4 });
                    }
                }
                catch (java.lang.Exception var5)
                {
                    var5.printStackTrace();
                    netManager.disconnect("disconnect.genericReason", new object[] { "Internal client error: " + var5.toString() });
                }
            }

        }

        public void disconnect()
        {
            disconnected = true;
            netManager.interrupt();
            netManager.disconnect("disconnect.closed", new object[0]);
        }

        public override void handleMobSpawn(LivingEntitySpawnS2CPacket var1)
        {
            double var2 = var1.xPosition / 32.0D;
            double var4 = var1.yPosition / 32.0D;
            double var6 = var1.zPosition / 32.0D;
            float var8 = var1.yaw * 360 / 256.0F;
            float var9 = var1.pitch * 360 / 256.0F;
            EntityLiving var10 = (EntityLiving)EntityRegistry.create(var1.type, mc.world);
            var10.trackedPosX = var1.xPosition;
            var10.trackedPosY = var1.yPosition;
            var10.trackedPosZ = var1.zPosition;
            var10.entityId = var1.entityId;
            var10.setPositionAndRotation(var2, var4, var6, var8, var9);
            var10.isMultiplayerEntity = true;
            worldClient.forceEntity(var1.entityId, var10);
            java.util.List var11 = var1.getMetadata();
            if (var11 != null)
            {
                var10.getDataWatcher().updateWatchedObjectsFromList(var11);
            }

        }

        public override void handleUpdateTime(WorldTimeUpdateS2CPacket var1)
        {
            mc.world.setTime(var1.time);
        }

        public override void handleSpawnPosition(PlayerSpawnPositionS2CPacket var1)
        {
            mc.player.setSpawnPos(new Vec3i(var1.xPosition, var1.yPosition, var1.zPosition));
            mc.world.getProperties().setSpawn(var1.xPosition, var1.yPosition, var1.zPosition);
        }

        public override void func_6497_a(EntityVehicleSetS2CPacket var1)
        {
            object var2 = getEntityByID(var1.entityId);
            Entity var3 = getEntityByID(var1.vehicleEntityId);
            if (var1.entityId == mc.player.entityId)
            {
                var2 = mc.player;
            }

            if (var2 != null)
            {
                ((Entity)var2).mountEntity(var3);
            }
        }

        public override void func_9447_a(EntityStatusS2CPacket var1)
        {
            Entity var2 = getEntityByID(var1.entityId);
            if (var2 != null)
            {
                var2.handleHealthUpdate(var1.entityStatus);
            }

        }

        private Entity getEntityByID(int var1)
        {
            return var1 == mc.player.entityId ? mc.player : worldClient.getEntity(var1);
        }

        public override void handleHealth(HealthUpdateS2CPacket var1)
        {
            mc.player.setHealth(var1.healthMP);
        }

        public override void func_9448_a(PlayerRespawnPacket var1)
        {
            if (var1.field_28048_a != mc.player.dimension)
            {
                field_1210_g = false;
                worldClient = new ClientWorld(this, worldClient.getProperties().getRandomSeed(), var1.field_28048_a);
                worldClient.isRemote = true;
                mc.changeWorld1(worldClient);
                mc.player.dimension = var1.field_28048_a;
                mc.displayGuiScreen(new GuiDownloadTerrain(this));
            }

            mc.respawn(true, var1.field_28048_a);
        }

        public override void func_12245_a(ExplosionS2CPacket var1)
        {
            Explosion var2 = new Explosion(mc.world, null, var1.explosionX, var1.explosionY, var1.explosionZ, var1.explosionSize);
            var2.destroyedBlockPositions = var1.destroyedBlockPositions;
            var2.doExplosionB(true);
        }

        public override void func_20087_a(OpenScreenS2CPacket var1)
        {
            if (var1.inventoryType == 0)
            {
                InventoryBasic var2 = new InventoryBasic(var1.windowTitle, var1.slotsCount);
                mc.player.openChestScreen(var2);
                mc.player.craftingInventory.syncId = var1.windowId;
            }
            else if (var1.inventoryType == 2)
            {
                BlockEntityFurnace var3 = new BlockEntityFurnace();
                mc.player.openFurnaceScreen(var3);
                mc.player.craftingInventory.syncId = var1.windowId;
            }
            else if (var1.inventoryType == 3)
            {
                BlockEntityDispenser var4 = new BlockEntityDispenser();
                mc.player.openDispenserScreen(var4);
                mc.player.craftingInventory.syncId = var1.windowId;
            }
            else if (var1.inventoryType == 1)
            {
                ClientPlayerEntity var5 = mc.player;
                mc.player.openCraftingScreen(MathHelper.floor_double(var5.x), MathHelper.floor_double(var5.y), MathHelper.floor_double(var5.z));
                mc.player.craftingInventory.syncId = var1.windowId;
            }

        }

        public override void func_20088_a(ScreenHandlerSlotUpdateS2CPacket var1)
        {
            if (var1.windowId == -1)
            {
                mc.player.inventory.setItemStack(var1.myItemStack);
            }
            else if (var1.windowId == 0 && var1.itemSlot >= 36 && var1.itemSlot < 45)
            {
                ItemStack var2 = mc.player.inventorySlots.getSlot(var1.itemSlot).getStack();
                if (var1.myItemStack != null && (var2 == null || var2.count < var1.myItemStack.count))
                {
                    var1.myItemStack.bobbingAnimationTime = 5;
                }

                mc.player.inventorySlots.setStackInSlot(var1.itemSlot, var1.myItemStack);
            }
            else if (var1.windowId == mc.player.craftingInventory.syncId)
            {
                mc.player.craftingInventory.setStackInSlot(var1.itemSlot, var1.myItemStack);
            }

        }

        public override void func_20089_a(ScreenHandlerAcknowledgementPacket var1)
        {
            ScreenHandler var2 = null;
            if (var1.windowId == 0)
            {
                var2 = mc.player.inventorySlots;
            }
            else if (var1.windowId == mc.player.craftingInventory.syncId)
            {
                var2 = mc.player.craftingInventory;
            }

            if (var2 != null)
            {
                if (var1.field_20030_c)
                {
                    var2.onAcknowledgementAccepted(var1.field_20028_b);
                }
                else
                {
                    var2.onAcknowledgementDenied(var1.field_20028_b);
                    addToSendQueue(new ScreenHandlerAcknowledgementPacket(var1.windowId, var1.field_20028_b, true));
                }
            }

        }

        public override void func_20094_a(InventoryS2CPacket var1)
        {
            if (var1.windowId == 0)
            {
                mc.player.inventorySlots.updateSlotStacks(var1.itemStack);
            }
            else if (var1.windowId == mc.player.craftingInventory.syncId)
            {
                mc.player.craftingInventory.updateSlotStacks(var1.itemStack);
            }

        }

        public override void handleSignUpdate(UpdateSignPacket var1)
        {
            if (mc.world.isPosLoaded(var1.x, var1.y, var1.z))
            {
                BlockEntity var2 = mc.world.getBlockEntity(var1.x, var1.y, var1.z);
                if (var2 is BlockEntitySign)
                {
                    BlockEntitySign var3 = (BlockEntitySign)var2;

                    for (int var4 = 0; var4 < 4; ++var4)
                    {
                        var3.texts[var4] = var1.text[var4];
                    }

                    var3.markDirty();
                }
            }

        }

        public override void func_20090_a(ScreenHandlerPropertyUpdateS2CPacket var1)
        {
            registerPacket(var1);
            if (mc.player.craftingInventory != null && mc.player.craftingInventory.syncId == var1.windowId)
            {
                mc.player.craftingInventory.setProperty(var1.progressBar, var1.progressBarValue);
            }

        }

        public override void handlePlayerInventory(EntityEquipmentUpdateS2CPacket var1)
        {
            Entity var2 = getEntityByID(var1.entityID);
            if (var2 != null)
            {
                var2.setEquipmentStack(var1.slot, var1.itemID, var1.itemDamage);
            }

        }

        public override void func_20092_a(CloseScreenS2CPacket var1)
        {
            mc.player.closeScreen();
        }

        public override void handleNotePlay(PlayNoteSoundS2CPacket var1)
        {
            mc.world.playNoteBlockActionAt(var1.xLocation, var1.yLocation, var1.zLocation, var1.instrumentType, var1.pitch);
        }

        public override void func_25118_a(GameStateChangeS2CPacket var1)
        {
            int var2 = var1.field_25019_b;
            if (var2 >= 0 && var2 < GameStateChangeS2CPacket.field_25020_a.Length && GameStateChangeS2CPacket.field_25020_a[var2] != null)
            {
                mc.player.sendMessage(GameStateChangeS2CPacket.field_25020_a[var2]);
            }

            if (var2 == 1)
            {
                worldClient.getProperties().setRaining(true);
                worldClient.setRainGradient(1.0F);
            }
            else if (var2 == 2)
            {
                worldClient.getProperties().setRaining(false);
                worldClient.setRainGradient(0.0F);
            }

        }

        public override void func_28116_a(MapUpdateS2CPacket var1)
        {
            if (var1.itemRawId == Item.MAP.id)
            {
                ItemMap.getMapState(var1.id, mc.world).func_28171_a(var1.updateData);
            }
            else
            {
                java.lang.System.@out.println("Unknown itemid: " + var1.id);
            }

        }

        public override void func_28115_a(WorldEventS2CPacket var1)
        {
            mc.world.worldEvent(var1.field_28050_a, var1.field_28053_c, var1.field_28052_d, var1.field_28051_e, var1.field_28049_b);
        }

        public override void func_27245_a(IncreaseStatS2CPacket var1)
        {
            ((EntityClientPlayerMP)mc.player).func_27027_b(Stats.Stats.getStatById(var1.field_27052_a), var1.field_27051_b);
        }

        public override bool isServerHandler()
        {
            return false;
        }
    }

}