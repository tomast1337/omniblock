namespace BetaSharp.Server;

/// <summary>
///     Decides how much chunk data one player may be sent this tick.
///     <para>
///         <c>docs/network-rewrite.md</c> §5.3. Chunk traffic is elastic and movement traffic is not,
///         so the only question that matters is whether we are ahead of the link — and if we are, to
///         stop, because everything queued behind a chunk waits for it.
///     </para>
///     <para>
///         <b>Closed on queue depth, not on an estimate.</b> §5.3 asks for a delay-based controller,
///         and the transport's own outgoing queue <em>is</em> the delay, before it has been converted
///         into one. A bandwidth estimate and a round-trip inflation threshold are both proxies for
///         this number, both need constants tuned per link, and both are wrong during the ramp. The
///         queue is exact, needs no estimator, and self-tunes to any link because a slow one drains
///         slowly and a fast one drains fast.
///     </para>
///     <para>
///         <b>Two bounds, doing different jobs.</b> The queue-depth mark keeps the link busy without
///         building a buffer. The per-tick byte cap stops one tick handing over everything before the
///         queue has had a chance to grow — without it, the first tick of a join enqueues the whole
///         view distance and the depth signal never gets to say no.
///     </para>
/// </summary>
public sealed class ChunkSendPacer
{
    /// <summary>
    ///     Stop when the transport already has this many world-data packets queued.
    ///     <para>
    ///         Sized as "enough to keep the link busy across a round trip, not enough to sit in".
    ///         Thirty-two packets is around 38 KB in flight, which on a 10 Mbit link is 30 ms — under
    ///         one tick, and well inside the 100 ms interpolation floor that would otherwise start
    ///         paying for it. On a slower link the same depth is more milliseconds, which is the
    ///         correct direction: fewer chunks in flight is exactly what a slow link wants.
    ///     </para>
    /// </summary>
    public const int MaxPendingPackets = 32;

    /// <summary>
    ///     Ceiling on chunk bytes handed over in one tick, whatever the queue says.
    ///     <para>
    ///         32 KB at 20 TPS is 640 KB/s, far above any real link, so this does not throttle
    ///         steady-state throughput. What it does is bound the <em>first</em> tick, before the
    ///         queue depth has anything to report: a view distance of 32 is 4,225 chunks, and
    ///         handing all 8 MB over at once makes every subsequent decision moot.
    ///     </para>
    /// </summary>
    public const int MaxBytesPerTick = 32 * 1024;

    private int _bytesThisTick;

    /// <summary>Bytes handed over during the tick now ending. For diagnostics.</summary>
    public int BytesLastTick { get; private set; }

    /// <summary>True when the last <see cref="TrySend" /> refused because the transport was behind.</summary>
    public bool Backpressured { get; private set; }

    /// <summary>Called once per tick, before any chunk is considered.</summary>
    public void BeginTick()
    {
        BytesLastTick = _bytesThisTick;
        _bytesThisTick = 0;
        Backpressured = false;
    }

    /// <summary>
    ///     Whether another chunk may go out.
    /// </summary>
    /// <param name="pendingPackets">
    ///     The transport's current queue depth for world data. Zero from a transport that cannot
    ///     report one — loopback, or a test fake — which correctly reads as "not backed up".
    /// </param>
    public bool CanSend(int pendingPackets)
    {
        if (_bytesThisTick >= MaxBytesPerTick)
        {
            return false;
        }

        if (pendingPackets >= MaxPendingPackets)
        {
            Backpressured = true;
            return false;
        }

        return true;
    }

    /// <summary>Books what a chunk actually cost, once it is known.</summary>
    public void Record(int bytes) => _bytesThisTick += bytes;
}
