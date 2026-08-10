using System.Diagnostics;

namespace OmniBlock.Util;

/// <summary>
///     The single monotonic time source for everything on the wire.
///     <para>
///         Every timestamp that crosses the network — the four time-sync stamps, the per-tick
///         snapshot stamp — must come from here. The clock-offset arithmetic in
///         <c>OmniBlock.Client.Network.ServerClock</c> maps one machine's reading of this clock onto
///         another's, and that mapping is only valid if both ends measure with the same units and
///         the same conversion. Two call sites computing "milliseconds" slightly differently would
///         produce an offset that silently absorbs the discrepancy.
///     </para>
///     <para>
///         The epoch is arbitrary and differs per process. That is fine: only differences between
///         readings from the same machine are meaningful, and the offset formula cancels the epochs.
///         It is deliberately <em>not</em> wall-clock — an NTP correction or a user changing the
///         system time would step a wall clock backwards mid-session.
///     </para>
/// </summary>
public static class MonotonicClock
{
    private const double TicksToMs = 1000.0;

    /// <summary>Milliseconds since this process's arbitrary epoch.</summary>
    public static long NowMs() => ToMs(Stopwatch.GetTimestamp());

    /// <summary>Raw ticks, for callers that need sub-millisecond resolution to take a difference.</summary>
    public static long NowTicks() => Stopwatch.GetTimestamp();

    /// <summary>Converts a <see cref="NowTicks" /> reading to milliseconds.</summary>
    public static long ToMs(long ticks) => (long)(ticks * (TicksToMs / Stopwatch.Frequency));

    /// <summary>Converts a difference of two <see cref="NowTicks" /> readings to milliseconds.</summary>
    public static double ElapsedMs(long startTicks, long endTicks) =>
        (endTicks - startTicks) * (TicksToMs / Stopwatch.Frequency);
}
