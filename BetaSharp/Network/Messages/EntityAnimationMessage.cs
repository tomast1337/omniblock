namespace BetaSharp.Network.Messages;

/// <summary>
///     A one-shot animation on an entity. Replaces <c>EntityAnimationPacket</c>.
/// </summary>
[WireMessage("betasharp:entity_animation")]
public sealed partial class EntityAnimationMessage : Message
{
    public override SendPriority Priority => SendPriority.High;

    [WireField]
    public int EntityId { get; set; }

    [WireField]
    public byte AnimationId { get; set; }

    public enum EntityAnimation : byte
    {
        SwingHand = 1,
        Hurt = 2,
        WakeUp = 3,
        Spawn = 4,
    }
}
