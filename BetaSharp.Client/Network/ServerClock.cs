using BetaSharp.Network;
using BetaSharp.Util;

namespace BetaSharp.Client.Network;

/// <summary>
///     NTP-style clock synchronisation over TCP, so the client and server agree on what "now" is
///     and remote entities can be interpolated on that shared timeline.
///     <para>
///         Phase 2 of <c>docs/time-sync-and-interpolation.md</c>. Ships with no visible change —
///         RTT, offset and jitter are exposed on the F3 overlay so they can be watched for a
///         session before anything depends on them.
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

    private long _appliedOffsetMs;
    private long _targetOffsetMs;
    private long _rttMedianMs;
    private long _jitterMs;
    private bool _synchronised;

    private uint _nextSequence;
    private long _firstProbeTime;
    private int _burstRemaining = BurstProbes;

    /// <summary>Ring buffer of accepted samples, newest replacing oldest.</summary>
    private readonly TimeSample[] _window = new TimeSample[WindowSize];

    private int _windowIndex;
    private int _windowCount;

    /// <summary>Outstanding probes. Sequence → T0.</summary>
    private readonly Dictionary<uint, long> _pending = [];

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
    public long ServerTimeMs => MonotonicNowMs() + _appliedOffsetMs;

    /// <summary>True once the login burst has completed and the first offset is applied.</summary>
    public bool Synchronised => _synchronised;

    /// <summary>Estimated round-trip time to the server, in milliseconds.</summary>
    public long RttMedianMs => _rttMedianMs;

    /// <summary>
    ///     Mean absolute deviation of RTT from its median. Feeds the interpolation-delay
    ///     calculation in <c>docs/time-sync-and-interpolation.md</c> §3.4.
    /// </summary>
    public long JitterMs => _jitterMs;

    /// <summary>
    ///     Server clock minus client clock. Positive means the server is ahead.
    /// </summary>
    public long OffsetMs => _appliedOffsetMs;

    /// <summary>
    ///     True when the last correction was a step rather than a slew, meaning every snapshot
    ///     buffer must be flushed.
    /// </summary>
    public bool NeedsSnapshotFlush { get; private set; }

    /// <summary>Consumed once after a step; resets itself.</summary>
    public bool ConsumeSnapshotFlush()
    {
        bool value = NeedsSnapshotFlush;
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
        long nowMs = MonotonicNowMs();

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
        if (!_pending.Remove(sequence, out long expectedT0) || expectedT0 != t0)
        {
            // A response that does not match an outstanding probe, or whose echoed T0 disagrees:
            // drop it rather than feeding stale data into the window.
            return;
        }

        long rtt = (t3 - t0) - (t2 - t1);

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

        long offset = ((t1 - t0) + (t2 - t3)) / 2;

        // A rejected outlier still advances the window and counts as a sample for the median, so
        // the rejection threshold itself tracks changing conditions. A sample far enough out is
        // excluded from the best-N selection but stays in the median baseline.
        bool outlier = _windowCount > 0 && rtt > (long)(_rttMedianMs * RejectRttMultiplier);

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
        uint seq = _nextSequence++;
        long t0 = MonotonicNowMs();

        // Drop probes old enough that a reply is no longer plausible. Without this, every lost
        // response leaks an entry for the life of the connection, and a peer that drops them all —
        // as happens when outgoing extended packets are gated off — grows the table unboundedly at
        // one entry per second.
        if (_pending.Count > 0)
        {
            foreach (uint stale in _pending
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

        // Median RTT from the full window, not just the best N. The rejection threshold (§2.2 step
        // 1) needs the uncontaminated median, and computing it from the same set it rejects against
        // would create a feedback loop.
        _rttMedianMs = MedianRtt();

        // Best BestCount non-outlier samples by RTT.
        TimeSample[] best = _window.Take(_windowCount).Where(s => !s.Outlier).OrderBy(s => s.Rtt).Take(BestCount).ToArray();

        // Degenerate: every sample was an outlier. Drop the outlier flag and select by RTT directly.
        if (best.Length == 0)
        {
            best = _window.Take(_windowCount).OrderBy(s => s.Rtt).Take(BestCount).ToArray();
        }

        long[] offsets = best.Select(s => s.Offset).Order().ToArray();

        int mid = offsets.Length / 2;
        _targetOffsetMs = offsets.Length % 2 == 0
            ? (offsets[mid - 1] + offsets[mid]) / 2
            : offsets[mid];

        // Jitter: mean absolute deviation from median RTT, over the accepted (non-outlier) samples.
        long[] acceptedRtts = _window.Take(_windowCount).Where(s => !s.Outlier).Select(s => s.Rtt).ToArray();
        if (acceptedRtts.Length == 0)
        {
            acceptedRtts = _window.Take(_windowCount).Select(s => s.Rtt).ToArray();
        }

        _jitterMs = (long)acceptedRtts.Average(r => Math.Abs(r - _rttMedianMs));

        // Apply: step or slew (§2.3).
        if (!_synchronised)
        {
            // First offset ever: set directly. No terrain is visible yet.
            _appliedOffsetMs = _targetOffsetMs;
            _synchronised = true;
            NeedsSnapshotFlush = true;
            return;
        }

        long error = _targetOffsetMs - _appliedOffsetMs;
        if (Math.Abs(error) > StepThresholdMs)
        {
            _appliedOffsetMs = _targetOffsetMs;
            NeedsSnapshotFlush = true;
        }
        else
        {
            _appliedOffsetMs += (long)Math.Clamp((double)error, -SlewStepMs, SlewStepMs);
        }
    }

    private long MedianRtt()
    {
        if (_windowCount == 0)
        {
            return 0;
        }

        long[] sorted = _window.Take(_windowCount).Select(s => s.Rtt).Order().ToArray();

        int mid = sorted.Length / 2;
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
