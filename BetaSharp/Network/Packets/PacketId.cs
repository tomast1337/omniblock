namespace BetaSharp.Network.Packets;

public enum PacketId : byte
{
    KeepAlive = 0,
    LoginHello = 1,
    Handshake = 2,
    ChatMessage = 3,
    WorldTimeUpdateS2C = 4,
    PlayerSpawnPositionS2C = 6,
    HealthUpdateS2C = 8,
    PlayerRespawn = 9,
    PlayerMove = 10,
    PlayerMovePositionAndOnGround = 11,
    PlayerMoveLookAndOnGround = 12,
    PlayerMoveFull = 13,
    PlayerSleepUpdateS2C = 17,
    PlayerSpawnS2C = 20,
    ItemEntitySpawnS2C = 21,
    EntitySpawnS2C = 23,
    LivingEntitySpawnS2C = 24,
    PaintingEntitySpawnS2C = 25,
    ChunkStatusUpdateS2C = 50,
    ChunkDataS2C = 51,
    ChunkDeltaUpdateS2C = 52,
    BlockUpdateS2C = 53,
    PlayNoteSoundS2C = 54,
    ExplosionS2C = 60,
    WorldEventS2C = 61,
    GameStateChangeS2C = 70,
    GlobalEntitySpawnS2C = 71,
    OpenScreenS2C = 100,
    CloseScreenS2C = 101,
    ScreenHandlerSlotUpdateS2C = 103,
    InventoryS2C = 104,
    ScreenHandlerPropertyUpdateS2C = 105,
    ScreenHandlerAcknowledgement = 106,
    UpdateSign = 130,
    MapUpdateS2C = 131,
    PlayerConnectionUpdateS2C = 132,
    PlayerGameModeUpdateS2C = 133,
    RegistryDataS2C = 134,
    FinishConfigurationS2C = 135,
    BundleS2C = 150,
    IncreaseStatS2C = 200,

    // 240-254 are reserved for the extensible message layer. Only these two IDs are spent on it:
    // everything else it carries travels inside OmniMessage, keyed by name rather than by number,
    // so adding messages never consumes another slot here. See Network/Messages/.
    MessageRegistrySyncS2C = 240,
    OmniMessage = 241,

    Disconnect = 255
}
