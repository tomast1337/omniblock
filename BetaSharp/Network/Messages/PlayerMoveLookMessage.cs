namespace BetaSharp.Network.Messages;

/// <summary>The player turned without moving. See <see cref="PlayerMoveMessage" /> for the priority.</summary>
[WireMessage("betasharp:player_move_look")]
public sealed partial class PlayerMoveLookMessage : Message, IPlayerMoveLook
{
    [WireField]
    public float Yaw { get; set; }

    [WireField]
    public float Pitch { get; set; }

    [WireField]
    public bool OnGround { get; set; }
}
