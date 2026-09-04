using OmniBlock.Network;
using OmniBlock.Util;

namespace OmniBlock.Client.Network;

/// <summary>
///     NTP-style clock synchronisation over TCP, so the client and server agree on what "now" is
///     and remote entities can be interpolated on that shared timeline.
///     <para>
///         RTT, offset and jitter are all exposed on the F3 overlay, so a link can be watched for a
///         session before trusting what interpolation derives from them.
///     </para>
///     <para>
///         <b>Login burst.</b> Eight probes ~100 ms apart. The lowest-RTT sample's offset is taken
///         as the initial value, set directly with no slew, because at login there is nothing
///         visible to slew against. The burst is hidden behind terrain load.
///     </para>
///     <para>
///         <b>Background pacer.</b> One probe per second after that. Each completed round trip is
///         filtered: samples whose RTT exceeds three times the running median are rejected (they are
///         a retransmit or a head-of-line stall, and their offset is contaminated by exactly the
///         path asymmetry the formula assumes away). The last 64 accepted samples are kept; the best
///         eight by RTT are selected; the <em>median</em> of their offsets is the target. Median
///         rather than mean, because one surviving bad sample cannot drag a median.
///     </para>
///     <para>
///         <b>Step versus slew.</b> The offset is never assigned directly once gameplay starts
///         unless the error exceeds 200 ms — that is a VM suspend, a laptop resume, or a server
///         restart, and slewing it would take minutes of visibly wrong world. Below 200 ms the
///         error is crystal drift (10–50 ppm, ~5 ms per 100 s) and the applied offset slews toward
///         the target at up to 5 ms per probe. The 5 ms ceiling has two orders of magnitude of
///         headroom over measured drift, so it is well below the threshold of visibility. On a step,
///         every snapshot buffer must be flushed — their timestamps are on the old timeline and
///         interpolating across the discontinuity would glide through the error instead of cutting.
///     </para>
/// </summary>
public sealed class ServerClock
{
    private const int BurstProbes = 8;
    private const int BackgroundIntervalMs = 1000;
    private const int WindowSize = 64;
    private const int BestCount = 8;
    private const double RejectRttMultiplier = 3.0;
    private const double SlewStepMs = 5.0;
    private const double StepThresholdMs = 200.0;

    /// <summary>
    ///     How long an outstanding probe is kept before it is assumed lost. Well past any RTT worth
    ///     accepting — a reply this late would be rejected by the outlier filter anyway.
    /// </summary>
    private const long PendingTimeoutMs = 30_000;

    /// <summary>Outstanding probes. Sequence → T0.</summary>
    private readonly Dictionary<uint, long> _pending = [];

    /// <summary>Ring buffer of accepted samples, newest replacing oldest.</summary>
    private readonly TimeSample[] _window = new TimeSample[WindowSize];

    private int _burstRemaining = BurstProbes;
    private long _firstProbeTime;

    private uint _nextSequence;
    private long _targetOffsetMs;
    private int _windowCount;

    private int _windowIndex;

    /// <summary>
    ///     Distribution of measured round-trip times, for the overlay.
    ///     <para>
    ///         The median and the mean absolute deviation this class computes are what the delay
    ///         formula needs, and they are a poor description of a link. A connection whose RTT is 30
    ///         ms nine times in ten and 300 ms otherwise has a small median and a jitter figure that
    ///         understates what interpolation actually has to absorb; the shape says so at a glance
    ///         and two scalars cannot. Reuses the arrival histogram's buckets, which are already
    ///         clustered where the decisions are.
    ///     </para>
    /// </summary>
    public PacketArrivalHistogram RttHistogram { get; } = new();

    // ---- queries ----

    /// <summary>
    ///     The server's clock, as best this client can estimate it. Only valid after
    ///     <see cref="Synchronised" /> is true; before that the login burst is still running and
    ///     the answer would be a degenerate guess.
    /// </summary>
    public long ServerTimeMs => MonotonicNowMs() + OffsetMs;

    /// <summary>True once the login burst has completed and the first offset is applied.</summary>
    public bool Synchronised { get; private set; }

    /// <summary>Estimated round-trip time to the server, in milliseconds.</summary>
    public long RttMedianMs { get; private set; }

    /// <summary>
    ///     Mean absolute deviation of RTT from its median. Feeds
    ///     <see cref="EntityInterpolator.NetworkJitterMs" />.
    /// </summary>
    public long JitterMs { get; private set; }

    /// <summary>
    ///     Server clock minus client clock. Positive means the server is ahead.
    /// </summary>
    public long OffsetMs { get; private set; }

    /// <summary>
    ///     True when the last correction was a step rather than a slew, meaning every snapshot
    ///     buffer must be flushed.
    /// </summary>
    public bool NeedsSnapshotFlush { get; private set; }

    /// <summary>Consumed once after a step; resets itself.</summary>
    public bool ConsumeSnapshotFlush()
    {
        var value = NeedsSnapshotFlush;
        NeedsSnapshotFlush = false;
        return value;
    }

    // ---- client side: produce probes and consume responses ----

    /// <summary>
    ///     Called each tick. Decides whether to send a probe and advances the burst state machine.
    ///     Returns a T0 + sequence if a probe should be sent, or null.
    /// </summary>
    public (uint Sequence, long ClientSendTime)? Poll()
    {
        var nowMs = MonotonicNowMs();

        if (_burstRemaining > 0)
        {
            // Fire one probe per Poll call during the burst. Poll is called from the client's
            // game tick (~50ms), so the implicit pacing is close to the target 100ms without a
            // timer that would also need stubbing in tests.
            _burstRemaining--;

            // Record the first probe's timestamp as the pacer baseline. Once the burst is
            // exhausted the background pacer takes over this field.
            if (_burstRemaining == BurstProbes - 1)
            {
                _firstProbeTime = nowMs;
            }

            return SendProbe();
        }

        // Background: one per second, reusing the same _firstProbeTime as the cadence timer.
        if (nowMs - _firstProbeTime >= BackgroundIntervalMs)
        {
            _firstProbeTime = nowMs;
            return SendProbe();
        }

        return null;
    }

    /// <summary>
    ///     Feeds a completed round trip into the filtering pipeline.
    ///     <para>
    ///         T3 was stamped by <c>Connection.Reading</c> on the read thread, so it is as close
    ///         to the wire as the architecture allows. The handler passes it in rather than stamping
    ///         its own, because the handler runs on the game thread up to a tick later.
    ///     </para>
    /// </summary>
    public void Complete(uint sequence, long t0, long t1, long t2, long t3)
    {
        if (!_pending.Remove(sequence, out var expectedT0) || expectedT0 != t0)
        {
            // A response that does not match an outstanding probe, or whose echoed T0 disagrees:
            // drop it rather than feeding stale data into the window.
            return;
        }

        var rtt = t3 - t0 - (t2 - t1);

        // RTT cannot be negative, and a zero or near-zero value is a degenerate probe — typically
        // a server that echoed before the client's own thread registered the send.
        if (rtt <= 0)
        {
            return;
        }

        // Recorded before the outlier filter runs. A rejected sample is exactly the tail worth
        // seeing: filtering it out of the estimate is right, and filtering it out of the picture
        // would hide the events the estimate is being protected from.
        RttHistogram.Record(rtt);

        var offset = (t1 - t0 + (t2 - t3)) / 2;

        // A rejected outlier still advances the window and counts as a sample for the median, so
        // the rejection threshold itself tracks changing conditions. A sample far enough out is
        // excluded from the best-N selection but stays in the median baseline.
        var outlier = _windowCount > 0 && rtt > (long)(RttMedianMs * RejectRttMultiplier);

        TimeSample sample = new(sequence, rtt, offset, outlier);

        // Push into ring buffer.
        _window[_windowIndex] = sample;
        _windowIndex = (_windowIndex + 1) % WindowSize;
        if (_windowCount < WindowSize)
        {
            _windowCount++;
        }

        UpdateEstimate();

        // The login burst completes after the low-RTT selection runs for the first time. See Poll:
        // by this point _synchronised is already true if the burst exhausted, so this is the
        // background path only.
    }

    // ---- server side: stamp the two server timestamps ----

    /// <summary>
    ///     Stamped on the server's read thread when the request arrives. Placed here rather than
    ///     duplicated so both peers use the same conversion, removing one variable from the
    ///     determinism surface.
    /// </summary>
    public static long StampT1() => MonotonicNowMs();

    /// <summary>
    ///     Stamped immediately before the response is written. As late as possible, so T2-T1
    ///     measures only the time the server held the probe.
    /// </summary>
    public static long StampT2() => MonotonicNowMs();

    /// <summary>
    ///     Client stamps T0 just before sending, and T3 on receipt.
    /// </summary>
    public static long StampT0() => MonotonicNowMs();

    public static long StampT3() => MonotonicNowMs();

    /// <summary>
    ///     Machine-local monotonic time in milliseconds, from an arbitrary per-process epoch.
    ///     The epoch differs per machine, which is fine — only <em>differences</em> between
    ///     timestamps from the same machine are meaningful. The offset computed from the four
    ///     timestamps cancels the epochs.
    /// </summary>
    public static long MonotonicNowMs() => MonotonicClock.NowMs();

    // ---- internal ----

    private (uint, long) SendProbe()
    {
        var seq = _nextSequence++;
        var t0 = MonotonicNowMs();

        // Drop probes old enough that a reply is no longer plausible. Without this, every lost
        // response leaks an entry for the life of the connection, and a peer that drops them all —
        // as happens when outgoing extended packets are gated off — grows the table unboundedly at
        // one entry per second.
        if (_pending.Count > 0)
        {
            foreach (var stale in _pending
                         .Where(p => t0 - p.Value > PendingTimeoutMs)
                         .Select(p => p.Key)
                         .ToArray())
            {
                _pending.Remove(stale);
            }
        }

        _pending[seq] = t0;
        return (seq, t0);
    }

    private void UpdateEstimate()
    {
        if (_windowCount == 0)
        {
            return;
        }

        // Median RTT from the full window, not just the best N. The rejection threshold below needs
        // an uncontaminated median, and computing it from the same set it rejects against would
        // create a feedback loop.
        RttMedianMs = MedianRtt();

        // Best BestCount non-outlier samples by RTT.
        var best = _window.Take(_windowCount).Where(s => !s.Outlier).OrderBy(s => s.Rtt).Take(BestCount).ToArray();

        // Degenerate: every sample was an outlier. Drop the outlier flag and select by RTT directly.
        if (best.Length == 0)
        {
            best = _window.Take(_windowCount).OrderBy(s => s.Rtt).Take(BestCount).ToArray();
        }

        var offsets = best.Select(s => s.Offset).Order().ToArray();

        var mid = offsets.Length / 2;
        _targetOffsetMs = offsets.Length % 2 == 0
            ? (offsets[mid - 1] + offsets[mid]) / 2
            : offsets[mid];

        // Jitter: mean absolute deviation from median RTT, over the accepted (non-outlier) samples.
        var acceptedRtts = _window.Take(_windowCount).Where(s => !s.Outlier).Select(s => s.Rtt).ToArray();
        if (acceptedRtts.Length == 0)
        {
            acceptedRtts = _window.Take(_windowCount).Select(s => s.Rtt).ToArray();
        }

        JitterMs = (long)acceptedRtts.Average(r => Math.Abs(r - RttMedianMs));

        // Apply: step on the first reading, slew afterwards.
        if (!Synchronised)
        {
            // First offset ever: set directly. No terrain is visible yet.
            OffsetMs = _targetOffsetMs;
            Synchronised = true;
            NeedsSnapshotFlush = true;
            return;
        }

        var error = _targetOffsetMs - OffsetMs;
        if (Math.Abs(error) > StepThresholdMs)
        {
            OffsetMs = _targetOffsetMs;
            NeedsSnapshotFlush = true;
        }
        else
        {
            OffsetMs += (long)Math.Clamp(error, -SlewStepMs, SlewStepMs);
        }
    }

    private long MedianRtt()
    {
        if (_windowCount == 0)
        {
            return 0;
        }

        var sorted = _window.Take(_windowCount).Select(s => s.Rtt).Order().ToArray();

        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2
            : sorted[mid];
    }

    private readonly record struct TimeSample(
        uint Sequence,
        long Rtt,
        long Offset,
        bool Outlier);
}
