using OmniBlock.Client.Rendering.Chunks;

namespace OmniBlock.Tests.Rendering;

public sealed class FrameTimingWindowTests
{
    [Fact]
    public void Snapshot_reports_recent_mean_and_nearest_rank_percentiles()
    {
        FrameTimingWindow window = new(4);
        window.Record(4);
        window.Record(1);
        window.Record(3);
        window.Record(2);

        var snapshot = window.Snapshot();

        Assert.Equal(4, snapshot.Samples);
        Assert.Equal(2, snapshot.LastMs);
        Assert.Equal(2.5, snapshot.AverageMs);
        Assert.Equal(2, snapshot.P50Ms);
        Assert.Equal(4, snapshot.P95Ms);
        Assert.Equal(4, snapshot.MaxMs);
    }

    [Fact]
    public void Full_window_discards_the_oldest_sample()
    {
        FrameTimingWindow window = new(3);
        window.Record(100);
        window.Record(1);
        window.Record(2);
        window.Record(3);

        var snapshot = window.Snapshot();

        Assert.Equal(3, snapshot.Samples);
        Assert.Equal(3, snapshot.LastMs);
        Assert.Equal(2, snapshot.AverageMs);
        Assert.Equal(2, snapshot.P50Ms);
        Assert.Equal(3, snapshot.P95Ms);
        Assert.Equal(3, snapshot.MaxMs);
    }

    [Fact]
    public void Invalid_samples_do_not_enter_the_window()
    {
        FrameTimingWindow window = new();
        window.Record(double.NaN);
        window.Record(double.PositiveInfinity);
        window.Record(-1);

        Assert.Equal(FrameTimingSnapshot.Empty, window.Snapshot());
    }
}
