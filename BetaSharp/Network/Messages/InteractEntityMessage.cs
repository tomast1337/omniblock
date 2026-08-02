namespace BetaSharp.Network.Messages;

/// <summary>
///     A click on another entity, carrying the instant the clicking player was rendering it at.
///     <para>
///         <c>PlayerInteractEntityC2SPacket</c> said which entity and which button, and that was
///         sufficient when the client drew entities
///         wherever the last packet put them. It no longer is: entities are interpolated, so the
///         target on the attacker's screen is somewhere between two snapshots at a render time the
///         server has no way to reconstruct — it varies per entity, and it moves as the delay ramps.
///     </para>
///     <para>
///         <b>The client states the time rather than the position.</b> A claimed position would be a
///         claimed hit, and there would be nothing left for the server to check. A claimed
///         <em>time</em> is checked against the server's own record of where the target was then, so
///         the worst a dishonest client achieves is to pick a different moment out of the second the
///         server is willing to rewind — which is the same second an honest client with that latency
///         already gets. See <c>EntityPositionHistory.MaxRewindMs</c> for where that bound is set.
///     </para>
///     <para>
///         A peer that does not speak this sends the legacy packet and is judged against the present,
///         which is what happened before this existed.
///     </para>
/// </summary>
[WireMessage("betasharp:interact_entity")]
public sealed partial class InteractEntityMessage : Message
{
    /// <summary>
    ///     A hit queued behind bulk traffic is a hit that arrives after the rewind window has moved
    ///     past the moment it describes.
    /// </summary>
    public override SendPriority Priority => SendPriority.High;

    [WireField]
    public int EntityId { get; set; }

    /// <summary>Same encoding as <c>PlayerInteractEntityC2SPacket.IsLeftClick</c>: 0 interacts, 1 attacks.</summary>
    [WireField]
    public byte Action { get; set; }

    /// <summary>
    ///     The server-clock instant the client was rendering the target at, from its synchronised
    ///     <c>ServerClock</c> less that entity's interpolation delay. Zero when the clock has not
    ///     synchronised yet, which the server reads as "no rewind" rather than as the epoch.
    /// </summary>
    [WireField]
    public long RenderTimeMs { get; set; }
}
