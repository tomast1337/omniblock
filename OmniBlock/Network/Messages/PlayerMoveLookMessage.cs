namespace OmniBlock.Network.Messages;

/// <summary>The player turned without moving. See <see cref="PlayerMoveMessage" /> for the priority.</summary>
[WireMessage("omniblock:player_move_look")]
public sealed partial class PlayerMoveLookMessage : Message, IPlayerMoveLook
{
    [WireField]
    public float Yaw { get; set; }

    [WireField]
    public float Pitch { get; set; }

    [WireField]
    public bool OnGround { get; set; }
}
