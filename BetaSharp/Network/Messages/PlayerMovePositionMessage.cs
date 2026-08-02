namespace BetaSharp.Network.Messages;

/// <summary>The player moved without turning. See <see cref="PlayerMoveMessage" /> for the priority.</summary>
[WireMessage("betasharp:player_move_position")]
public sealed partial class PlayerMovePositionMessage : Message, IPlayerMovePosition
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
    public bool OnGround { get; set; }
}
