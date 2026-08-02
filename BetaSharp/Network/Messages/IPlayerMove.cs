namespace BetaSharp.Network.Messages;

/// <summary>
///     What every player-movement message carries.
///     <para>
///         The family is four messages rather than one because a client that only turned its head
///         should not pay for a position, and one that only stepped off a block should pay for
///         neither. Which of the four is sent is decided per tick from what actually changed.
///     </para>
///     <para>
///         The two handlers that consume them — one per side — branch on
///         <see cref="IPlayerMovePosition" /> and <see cref="IPlayerMoveLook" /> rather than on the
///         concrete type, so the four variants are a wire-encoding concern and not a behavioural
///         one. Collapsing them into a single message with a presence mask belongs to the movement
///         rewrite, which has to revisit what is sent at all.
///     </para>
/// </summary>
public interface IPlayerMove
{
    bool OnGround { get; }
}

/// <summary>
///     A movement message that carries a position.
///     <para>
///         Settable because the receiving client corrects a server-sent position against its own
///         bounding box and echoes the message back, which is how the two ends agree on where a
///         teleport actually put the player.
///     </para>
/// </summary>
public interface IPlayerMovePosition : IPlayerMove
{
    double X { get; set; }

    double Y { get; set; }

    double Z { get; set; }

    /// <summary>
    ///     Eye level. The server reads the gap between this and <see cref="Y" /> as the player's
    ///     stance and disconnects a client whose stance is impossible.
    /// </summary>
    double EyeHeight { get; set; }
}

/// <summary>A movement message that carries a facing.</summary>
public interface IPlayerMoveLook : IPlayerMove
{
    float Yaw { get; }

    float Pitch { get; }
}
