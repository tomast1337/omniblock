namespace BetaSharp.Network.Messages;

/// <summary>
///     The player neither moved nor turned, but its footing changed. See <see cref="IPlayerMove" />
///     for why this is worth its own message.
///     <para>
///         Normal priority, like the rest of the family, and deliberately not high. Movement is
///         latency-sensitive, but a server-sent position that overtakes the chunk batch it belongs
///         after puts the player in terrain the client has not loaded — so it has to stay in the
///         same ordering domain as world data.
///     </para>
/// </summary>
[WireMessage("betasharp:player_move")]
public sealed partial class PlayerMoveMessage : Message, IPlayerMove
{
    [WireField]
    public bool OnGround { get; set; }
}
