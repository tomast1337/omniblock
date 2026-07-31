namespace BetaSharp.Network;

/// <summary>
///     Distribution of the gap between successive packet arrivals on one connection.
///     <para>
///         Phase 1 of <c>docs/time-sync-and-interpolation.md</c>: the interpolation delay must
///         exceed the typical TCP head-of-line stall or the snapshot buffer starves, and sizing it
///         at the worst case is a permanent latency tax paid on every frame to hide a rare event.
///         Both numbers have to be measured. This is the instrument.
///     </para>
///     <para>
///         Fixed logarithmic buckets rather than stored samples: recording is one array increment
///         with no allocation and no growth, which is what makes it safe to leave on permanently on
///         the read thread. The cost is that percentiles are bucket-resolution, which
///         <see cref="PercentileMs" /> is explicit about.
///     </para>
/// </summary>
public sealed class PacketArrivalHistogram
{
    /// <summary>
    ///     Upper edges in milliseconds, the last being everything above. Clustered around the 50 ms
    ///     server tick and the 100-500 ms band where chunk-transfer stalls are expected, since those
    ///     are the two regions any decision gets made from.
    /// </summary>
    private static readonly double[] s_upperBounds =
        [1, 2, 5, 10, 20, 35, 50, 75, 100, 150, 200, 300, 500, 750, 1000, double.PositiveInfinity];

    private readonly long[] _buckets = new long[s_upperBounds.Length];

    private long _count;
    private double _maxMs;
    private double _totalMs;

    public long Count => Interlocked.Read(ref _count);

    /// <summary>Largest gap seen. The tail that decides whether extrapolation is needed at all.</summary>
    public double MaxMs => Volatile.Read(ref _maxMs);

    public double MeanMs
    {
        get
        {
            long count = Count;
            return count == 0 ? 0.0 : Volatile.Read(ref _totalMs) / count;
        }
    }

    public static IReadOnlyList<double> UpperBounds => s_upperBounds;

    /// <summary>
    ///     Records one inter-arrival gap. Called from <c>Connection</c>'s read thread, which is the
    ///     only writer; readers see values that may lag by one sample, which is immaterial for a
    ///     diagnostic.
    /// </summary>
    public void Record(double intervalMs)
    {
        if (double.IsNaN(intervalMs) || intervalMs < 0.0)
        {
            return;
        }

        int bucket = s_upperBounds.Length - 1;
        for (int i = 0; i < s_upperBounds.Length; i++)
        {
            if (intervalMs <= s_upperBounds[i])
            {
                bucket = i;
                break;
            }
        }

        Interlocked.Increment(ref _buckets[bucket]);
        Interlocked.Increment(ref _count);

        // Racy read-modify-write on a diagnostic accumulator: a lost update costs one sample's
        // contribution to the mean, which is not worth a lock on the read path.
        Volatile.Write(ref _totalMs, Volatile.Read(ref _totalMs) + intervalMs);

        if (intervalMs > Volatile.Read(ref _maxMs))
        {
            Volatile.Write(ref _maxMs, intervalMs);
        }
    }

    /// <summary>
    ///     Upper edge of the bucket containing the <paramref name="percentile" />-th sample.
    ///     <para>
    ///         An upper bound, not an interpolated estimate — p95 of 200 means "95% of gaps were at
    ///         most 200 ms", not "p95 is exactly 200 ms". For choosing an interpolation delay an
    ///         upper bound is the conservative direction and therefore the right one.
    ///     </para>
    /// </summary>
    public double PercentileMs(double percentile)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(percentile);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percentile, 100.0);

        long total = Count;
        if (total == 0)
        {
            return 0.0;
        }

        long target = (long)Math.Ceiling(total * percentile / 100.0);
        long seen = 0;

        for (int i = 0; i < _buckets.Length; i++)
        {
            seen += Interlocked.Read(ref _buckets[i]);
            if (seen >= target)
            {
                return s_upperBounds[i];
            }
        }

        return s_upperBounds[^1];
    }

    /// <summary>Bucket counts, aligned with <see cref="UpperBounds" />.</summary>
    public long[] Snapshot()
    {
        long[] copy = new long[_buckets.Length];
        for (int i = 0; i < _buckets.Length; i++)
        {
            copy[i] = Interlocked.Read(ref _buckets[i]);
        }

        return copy;
    }

    public void Reset()
    {
        for (int i = 0; i < _buckets.Length; i++)
        {
            Interlocked.Exchange(ref _buckets[i], 0);
        }

        Interlocked.Exchange(ref _count, 0);
        Volatile.Write(ref _maxMs, 0.0);
        Volatile.Write(ref _totalMs, 0.0);
    }
}
