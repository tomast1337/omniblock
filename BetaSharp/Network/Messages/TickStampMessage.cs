namespace BetaSharp.Network.Messages;

/// <summary>
///     Announces the server-clock instant that the entity updates following it describe.
///     <para>
///         The one protocol change interpolation depends on: without a server timestamp the client
///         can only guess an update's age from its arrival time, which is exactly the quantity a
///         head-of-line stall corrupts.
///     </para>
///     <para>
///         <b>One stamp per tick, not per entity.</b> Every entity update produced by a single
///         simulation tick describes the same instant, so a per-entity timestamp would be eight
///         bytes of redundancy per entity per tick and no more accurate. The stream delivers in
///         order, so a stamp sent ahead of the batch unambiguously covers everything until the next
///         stamp.
///     </para>
///     <para>
///         <b>The value is the simulation instant, not the send instant.</b> Positions change in
///         <c>BetaSharpServer.Tick</c>; the entity tracker broadcasts the resulting deltas later, in
///         <c>TickFixed</c>. Stamping at send time would fold that scheduling gap into the timestamp
///         and jitter the interpolation by however long the two loops happened to drift apart.
///         <see cref="Message.TransportSentAtMs" /> is deliberately not used here — it would be the
///         send instant, which is the wrong quantity.
///     </para>
/// </summary>
[WireMessage("betasharp:tick_stamp")]
public sealed partial class TickStampMessage : Message
{
    /// <summary>A stamp that arrives late drags the whole interpolation timeline with it.</summary>
    public override SendPriority Priority => SendPriority.High;

    /// <summary>
    ///     The server's <see cref="Util.MonotonicClock" /> reading at the start of the simulation
    ///     tick whose updates follow. Same clock domain as the time-sync T1/T2 stamps, which is what
    ///     makes it comparable to the client's estimate of server time.
    /// </summary>
    [WireField]
    public long ServerTimeMs { get; set; }
}
