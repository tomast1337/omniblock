namespace OmniBlock.Network.Messages;

/// <summary>
///     A one-off visual or audible event on an entity — hurt, death, a wolf shaking off water.
///     Replaces <c>EntityStatusS2CPacket</c>.
/// </summary>
[WireMessage("omniblock:entity_status")]
public sealed partial class EntityStatusMessage : Message
{
    [WireField]
    public int EntityId { get; set; }

    [WireField]
    public sbyte Status { get; set; }

    /// <summary>
    ///     The named statuses. Sparse, and deliberately not the field's type: the wire carries
    ///     values this enum has no name for, and decoding into it would be a lie the compiler
    ///     believes.
    /// </summary>
    public enum EntityState : byte
    {
        Hurt = 2,
        Death = 3,
        WolfSmokeFx = 6,
        WolfHeartsFx = 7,
        WolfShaking = 8,
    }
}
