using BetaSharp.Network;

namespace BetaSharp.Tests.Network;

/// <summary>
///     Phase 1 of <c>docs/time-sync-and-interpolation.md</c>. These numbers choose the interpolation
///     delay, so an off-by-one in the percentile walk would quietly mis-size it.
/// </summary>
public sealed class PacketArrivalHistogramTests
{
    [Fact]
    public void An_empty_histogram_reports_zero_rather_than_dividing_by_it()
    {
        PacketArrivalHistogram histogram = new();

        Assert.Equal(0, histogram.Count);
        Assert.Equal(0.0, histogram.MeanMs);
        Assert.Equal(0.0, histogram.MaxMs);
        Assert.Equal(0.0, histogram.PercentileMs(95));
    }

    [Fact]
    public void Percentile_returns_the_upper_edge_of_the_containing_bucket()
    {
        // 100 samples at 3 ms all land in the (2, 5] bucket, so every percentile is that edge.
        PacketArrivalHistogram histogram = new();
        for (int i = 0; i < 100; i++)
        {
            histogram.Record(3.0);
        }

        Assert.Equal(5.0, histogram.PercentileMs(50));
        Assert.Equal(5.0, histogram.PercentileMs(95));
        Assert.Equal(5.0, histogram.PercentileMs(100));
    }

    [Fact]
    public void A_long_tail_moves_p99_without_moving_p50()
    {
        // The shape this whole exercise exists to detect: a stream that is mostly fine but stalls
        // occasionally. Sizing the delay off the mean would miss it entirely.
        PacketArrivalHistogram histogram = new();

        for (int i = 0; i < 980; i++)
        {
            histogram.Record(15.0);      // (10, 20]
        }

        for (int i = 0; i < 20; i++)
        {
            histogram.Record(450.0);     // (300, 500]  -- a 2% tail
        }

        Assert.Equal(20.0, histogram.PercentileMs(50));
        Assert.Equal(20.0, histogram.PercentileMs(95));
        Assert.Equal(500.0, histogram.PercentileMs(99));
        Assert.Equal(450.0, histogram.MaxMs);
    }

    [Fact]
    public void A_percentile_landing_exactly_on_a_bucket_boundary_stays_in_that_bucket()
    {
        // 990 of 1000 samples is exactly 99%, so p99 is the fast bucket, not the tail. Worth
        // pinning: the off-by-one here is the difference between a 20 ms and a 500 ms delay.
        PacketArrivalHistogram histogram = new();

        for (int i = 0; i < 990; i++)
        {
            histogram.Record(15.0);
        }

        for (int i = 0; i < 10; i++)
        {
            histogram.Record(450.0);
        }

        Assert.Equal(20.0, histogram.PercentileMs(99));
        Assert.Equal(500.0, histogram.PercentileMs(99.5));
    }

    [Fact]
    public void Percentiles_are_monotonic_across_a_spread_distribution()
    {
        PacketArrivalHistogram histogram = new();
        for (int i = 1; i <= 1000; i++)
        {
            histogram.Record(i % 600);
        }

        Assert.True(histogram.PercentileMs(50) <= histogram.PercentileMs(95));
        Assert.True(histogram.PercentileMs(95) <= histogram.PercentileMs(99));
        Assert.True(histogram.PercentileMs(99) <= histogram.PercentileMs(100));
    }

    [Fact]
    public void Values_past_the_last_bound_land_in_the_overflow_bucket()
    {
        PacketArrivalHistogram histogram = new();
        histogram.Record(50_000.0);

        Assert.Equal(1, histogram.Count);
        Assert.Equal(50_000.0, histogram.MaxMs);
        Assert.Equal(double.PositiveInfinity, histogram.PercentileMs(100));
    }

    [Fact]
    public void Mean_and_max_track_the_recorded_values()
    {
        PacketArrivalHistogram histogram = new();
        histogram.Record(10.0);
        histogram.Record(20.0);
        histogram.Record(60.0);

        Assert.Equal(3, histogram.Count);
        Assert.Equal(30.0, histogram.MeanMs, precision: 6);
        Assert.Equal(60.0, histogram.MaxMs);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1.0)]
    public void Nonsense_intervals_are_ignored_rather_than_skewing_the_distribution(double value)
    {
        // A clock going backwards across a suspend would otherwise poison every percentile.
        PacketArrivalHistogram histogram = new();
        histogram.Record(value);

        Assert.Equal(0, histogram.Count);
    }

    [Fact]
    public void Snapshot_totals_match_the_sample_count()
    {
        PacketArrivalHistogram histogram = new();
        histogram.Record(0.5);
        histogram.Record(30.0);
        histogram.Record(900.0);

        long[] buckets = histogram.Snapshot();

        Assert.Equal(PacketArrivalHistogram.UpperBounds.Count, buckets.Length);
        Assert.Equal(histogram.Count, buckets.Sum());
    }

    [Fact]
    public void Reset_clears_every_accumulator()
    {
        PacketArrivalHistogram histogram = new();
        histogram.Record(120.0);
        histogram.Reset();

        Assert.Equal(0, histogram.Count);
        Assert.Equal(0.0, histogram.MaxMs);
        Assert.Equal(0.0, histogram.MeanMs);
        Assert.All(histogram.Snapshot(), count => Assert.Equal(0, count));
    }

    [Fact]
    public void Concurrent_recording_loses_no_samples()
    {
        // The read thread is the only writer in production, but Count and the buckets are read
        // concurrently from the game thread, so the increments must be atomic.
        PacketArrivalHistogram histogram = new();

        Parallel.For(0, 8, _ =>
        {
            for (int i = 0; i < 1000; i++)
            {
                histogram.Record(25.0);
            }
        });

        Assert.Equal(8000, histogram.Count);
        Assert.Equal(8000, histogram.Snapshot().Sum());
    }
}
