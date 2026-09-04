using OmniBlock.Registries;

namespace OmniBlock.Network.Messages;

/// <summary>
///     Every message type whose <c>Read</c>/<c>Write</c>/<c>Size</c> live in a <c>*.Wire.cs</c>
///     companion file. Registering a new one here is the other half of adding such a file — nothing
///     discovers this list automatically.
/// </summary>
internal static class MessageRegistrations
{
    public static void RegisterAll(MessageRegistry registry, IItemRuntimeView items)
    {
        registry.Register(BlockUpdateMessage.Id, 1, static () => new BlockUpdateMessage());
        registry.Register(ChatMessage.Id, 1, static () => new ChatMessage());
        registry.Register(ChunkDataMessage.Id, 1, static () => new ChunkDataMessage());
        registry.Register(ChunkStatusUpdateMessage.Id, 1, static () => new ChunkStatusUpdateMessage());
        registry.Register(ChunkUnchangedMessage.Id, 1, static () => new ChunkUnchangedMessage());
        registry.Register(ClickSlotMessage.Id, 1, () => new ClickSlotMessage(items));
        registry.Register(ClientCommandMessage.Id, 1, static () => new ClientCommandMessage());
        registry.Register(CloseScreenMessage.Id, 1, static () => new CloseScreenMessage());
        registry.Register(DisconnectMessage.Id, 1, static () => new DisconnectMessage());
        registry.Register(EntityAnimationMessage.Id, 1, static () => new EntityAnimationMessage());
        registry.Register(EntityDataMessage.Id, 1, static () => new EntityDataMessage());
        registry.Register(EntityDestroyMessage.Id, 1, static () => new EntityDestroyMessage());
        registry.Register(EntityEquipmentMessage.Id, 1, static () => new EntityEquipmentMessage());
        registry.Register(EntityMoveMessage.Id, 1, static () => new EntityMoveMessage());
        registry.Register(EntitySpawnMessage.Id, 1, static () => new EntitySpawnMessage());
        registry.Register(EntityStatusMessage.Id, 1, static () => new EntityStatusMessage());
        registry.Register(EntityTeleportMessage.Id, 1, static () => new EntityTeleportMessage());
        registry.Register(EntityVehicleMessage.Id, 1, static () => new EntityVehicleMessage());
        registry.Register(EntityVelocityMessage.Id, 1, static () => new EntityVelocityMessage());
        registry.Register(FinishConfigurationMessage.Id, 1, static () => new FinishConfigurationMessage());
        registry.Register(GameStateChangeMessage.Id, 1, static () => new GameStateChangeMessage());
        registry.Register(GlobalEntitySpawnMessage.Id, 1, static () => new GlobalEntitySpawnMessage());
        registry.Register(HealthUpdateMessage.Id, 1, static () => new HealthUpdateMessage());
        registry.Register(IncreaseStatMessage.Id, 1, static () => new IncreaseStatMessage());
        registry.Register(InteractBlockMessage.Id, 1, () => new InteractBlockMessage(items));
        registry.Register(InteractEntityMessage.Id, 1, static () => new InteractEntityMessage());
        registry.Register(InventoryMessage.Id, 1, () => new InventoryMessage(items));
        registry.Register(ItemEntitySpawnMessage.Id, 1, static () => new ItemEntitySpawnMessage());
        registry.Register(ItemPickupMessage.Id, 1, static () => new ItemPickupMessage());
        registry.Register(KeepAliveMessage.Id, 1, static () => new KeepAliveMessage());
        registry.Register(LightSectionsMessage.Id, 1, static () => new LightSectionsMessage());
        registry.Register(LivingEntitySpawnMessage.Id, 1, static () => new LivingEntitySpawnMessage());
        registry.Register(MapUpdateMessage.Id, 1, static () => new MapUpdateMessage());
        registry.Register(OpenScreenMessage.Id, 1, static () => new OpenScreenMessage());
        registry.Register(PaintingSpawnMessage.Id, 1, static () => new PaintingSpawnMessage());
        registry.Register(PlayNoteSoundMessage.Id, 1, static () => new PlayNoteSoundMessage());
        registry.Register(PlayerActionMessage.Id, 1, static () => new PlayerActionMessage());
        registry.Register(PlayerConnectionUpdateMessage.Id, 1, static () => new PlayerConnectionUpdateMessage());
        registry.Register(PlayerGameModeUpdateMessage.Id, 1, static () => new PlayerGameModeUpdateMessage());
        registry.Register(PlayerInputMessage.Id, 1, static () => new PlayerInputMessage());
        registry.Register(PlayerMoveFullMessage.Id, 1, static () => new PlayerMoveFullMessage());
        registry.Register(PlayerMoveLookMessage.Id, 1, static () => new PlayerMoveLookMessage());
        registry.Register(PlayerMoveMessage.Id, 1, static () => new PlayerMoveMessage());
        registry.Register(PlayerMovePositionMessage.Id, 1, static () => new PlayerMovePositionMessage());
        registry.Register(PlayerRespawnMessage.Id, 1, static () => new PlayerRespawnMessage());
        registry.Register(PlayerSleepUpdateMessage.Id, 1, static () => new PlayerSleepUpdateMessage());
        registry.Register(PlayerSpawnMessage.Id, 1, static () => new PlayerSpawnMessage());
        registry.Register(PlayerSpawnPositionMessage.Id, 1, static () => new PlayerSpawnPositionMessage());
        registry.Register(RegionDataMessage.Id, 1, static () => new RegionDataMessage());
        registry.Register(ScreenHandlerAckMessage.Id, 1, static () => new ScreenHandlerAckMessage());
        registry.Register(ScreenHandlerPropertyMessage.Id, 1, static () => new ScreenHandlerPropertyMessage());
        registry.Register(ScreenHandlerSlotMessage.Id, 1, () => new ScreenHandlerSlotMessage(items));
        registry.Register(SelectedSlotMessage.Id, 1, static () => new SelectedSlotMessage());
        registry.Register(ServerStatusMessage.Id, 1, static () => new ServerStatusMessage());
        registry.Register(SnapshotAckMessage.Id, 1, static () => new SnapshotAckMessage());
        registry.Register(TickStampMessage.Id, 1, static () => new TickStampMessage());
        registry.Register(TimeSyncRequestMessage.Id, 1, static () => new TimeSyncRequestMessage());
        registry.Register(TimeSyncResponseMessage.Id, 1, static () => new TimeSyncResponseMessage());
        registry.Register(UpdateSignMessage.Id, 1, static () => new UpdateSignMessage());
        registry.Register(WorldEventMessage.Id, 1, static () => new WorldEventMessage());
        registry.Register(WorldTimeUpdateMessage.Id, 1, static () => new WorldTimeUpdateMessage());
    }
}
