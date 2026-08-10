namespace OmniBlock.Network.Messages;

/// <summary>
///     The player both moved and turned, and the only variant the server ever sends: a teleport has
///     to state both. See <see cref="PlayerMoveMessage" /> for the priority.
/// </summary>
[WireMessage("omniblock:player_move_full")]
public sealed partial class PlayerMoveFullMessage : Message, IPlayerMovePosition, IPlayerMoveLook
{
    [WireField]
    public double X { get; set; }

    [WireField]
    public double Y { get; set; }

    [WireField]
    public double EyeHeight { get; set; }

    [WireField]
    public double Z { get; set; }

    [WireField]
    public float Yaw { get; set; }

    [WireField]
    public float Pitch { get; set; }

    [WireField]
    public bool OnGround { get; set; }
}
