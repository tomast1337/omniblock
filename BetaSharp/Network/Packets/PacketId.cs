namespace BetaSharp.Network.Packets;

public enum PacketId : byte
{
    LoginHello = 1,
    Handshake = 2,
    WorldTimeUpdateS2C = 4,
    PlayerMove = 10,
    PlayerMovePositionAndOnGround = 11,
    PlayerMoveLookAndOnGround = 12,
    PlayerMoveFull = 13,
    ChunkStatusUpdateS2C = 50,
    ChunkDataS2C = 51,
    ChunkDeltaUpdateS2C = 52,
    BlockUpdateS2C = 53,
    PlayNoteSoundS2C = 54,
    ExplosionS2C = 60,
    WorldEventS2C = 61,
    MapUpdateS2C = 131,
    BundleS2C = 150,

    // 240-254 are reserved for the extensible message layer. Only these two IDs are spent on it:
    // everything else it carries travels inside OmniMessage, keyed by name rather than by number,
    // so adding messages never consumes another slot here. See Network/Messages/.
    MessageRegistrySyncS2C = 240,
    OmniMessage = 241
}
