using betareborn.Network.Packets;
using betareborn.Network.Packets.C2SPlay;
using betareborn.Network.Packets.Play;
using betareborn.Network.Packets.S2CPlay;

namespace betareborn.Network
{
    public abstract class NetHandler
    {
        public abstract bool isServerHandler();

        public virtual void handleMapChunk(ChunkDataS2CPacket var1)
        {
        }

        public virtual void registerPacket(Packet var1)
        {
        }

        public virtual void handleErrorMessage(string var1, object[] var2)
        {
        }

        public virtual void handleKickDisconnect(DisconnectPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleLogin(LoginHelloPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleFlying(PlayerMovePacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleMultiBlockChange(ChunkDeltaUpdateS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleBlockDig(PlayerActionC2SPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleBlockChange(BlockUpdateS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handlePreChunk(ChunkStatusUpdateS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleNamedEntitySpawn(PlayerSpawnS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleEntity(EntityS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleEntityTeleport(EntityPositionS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handlePlace(PlayerInteractBlockC2SPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleBlockItemSwitch(UpdateSelectedSlotC2SPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleDestroyEntity(EntityDestroyS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handlePickupSpawn(ItemEntitySpawnS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleCollect(ItemPickupAnimationS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleChat(ChatMessagePacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleVehicleSpawn(EntitySpawnS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleArmAnimation(EntityAnimationPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_21147_a(ClientCommandC2SPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleHandshake(HandshakePacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleMobSpawn(LivingEntitySpawnS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleUpdateTime(WorldTimeUpdateS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleSpawnPosition(PlayerSpawnPositionS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_6498_a(EntityVelocityUpdateS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_21148_a(EntityTrackerUpdateS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_6497_a(EntityVehicleSetS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleUseEntity(PlayerInteractEntityC2SPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_9447_a(EntityStatusS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleHealth(HealthUpdateS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_9448_a(PlayerRespawnPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_12245_a(ExplosionS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_20087_a(OpenScreenS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_20092_a(CloseScreenS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_20091_a(ClickSlotC2SPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_20088_a(ScreenHandlerSlotUpdateS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_20094_a(InventoryS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleSignUpdate(UpdateSignPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_20090_a(ScreenHandlerPropertyUpdateS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handlePlayerInventory(EntityEquipmentUpdateS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_20089_a(ScreenHandlerAcknowledgementPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_21146_a(PaintingEntitySpawnS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleNotePlay(PlayNoteSoundS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_27245_a(IncreaseStatS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_22186_a(PlayerSleepUpdateS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_22185_a(PlayerInputC2SPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_25118_a(GameStateChangeS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void handleWeather(GlobalEntitySpawnS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_28116_a(MapUpdateS2CPacket var1)
        {
            registerPacket(var1);
        }

        public virtual void func_28115_a(WorldEventS2CPacket var1)
        {
            registerPacket(var1);
        }
    }

}