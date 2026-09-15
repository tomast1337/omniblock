using OmniBlock.Client.Rendering.Core.WebGPU;

namespace OmniBlock.Tests.Rendering;

public sealed class GpuFrameProfilerTests
{
    [Fact]
    public void Aggregates_reopened_physical_passes_into_logical_categories()
    {
        ulong[] values =
        [
            100, 160, // first world segment
            170, 210, // cloud capture, still world
            220, 250, // interface
            260, 300  // composite
        ];
        GpuFrameProfiler.QueryRange[] ranges =
        [
            new(GpuPassCategory.World, 0, 1),
            new(GpuPassCategory.World, 2, 3),
            new(GpuPassCategory.Interface, 4, 5),
            new(GpuPassCategory.Composite, 6, 7)
        ];

        var snapshot = GpuFrameProfiler.BuildSnapshot(17, "available", 2.0, values, ranges);

        Assert.Equal(17, snapshot.Frame);
        Assert.Equal(200UL, snapshot.RenderSpanRawTicks);
        Assert.Equal(0.0004, snapshot.RenderSpanMilliseconds);
        Assert.Equal(100UL, snapshot.World.RawTicks);
        Assert.Equal(0.0002, snapshot.World.Milliseconds);
        Assert.Equal(30UL, snapshot.Interface.RawTicks);
        Assert.Equal(40UL, snapshot.Composite.RawTicks);
        Assert.Equal(0UL, snapshot.FirstPersonHand.RawTicks);
    }

    [Fact]
    public void Keeps_raw_ticks_when_native_timestamp_period_is_unknown()
    {
        ulong[] values = [10, 42];
        GpuFrameProfiler.QueryRange[] ranges = [new(GpuPassCategory.World, 0, 1)];

        var snapshot = GpuFrameProfiler.BuildSnapshot(1, "raw ticks only", null, values, ranges);

        Assert.Equal(32UL, snapshot.RenderSpanRawTicks);
        Assert.Null(snapshot.RenderSpanMilliseconds);
        Assert.Equal(32UL, snapshot.World.RawTicks);
        Assert.Null(snapshot.World.Milliseconds);
    }

    [Fact]
    public void Rejects_wrapped_or_invalid_ranges_instead_of_underflowing()
    {
        ulong[] values = [50, 25];
        GpuFrameProfiler.QueryRange[] ranges = [new(GpuPassCategory.World, 0, 1)];

        var snapshot = GpuFrameProfiler.BuildSnapshot(1, "available", 1.0, values, ranges);

        Assert.Equal(0UL, snapshot.RenderSpanRawTicks);
        Assert.Equal(0UL, snapshot.World.RawTicks);
    }

    [Theory]
    [InlineData(1UL, 1.0, 0.000001)]
    [InlineData(1_000_000UL, 1.0, 1.0)]
    [InlineData(12_000UL, 2.5, 0.03)]
    public void Converts_timestamp_ticks_using_the_reported_period(
        ulong ticks, double periodNanoseconds, double expectedMilliseconds)
    {
        Assert.Equal(expectedMilliseconds,
            GpuFrameProfiler.ToMilliseconds(ticks, periodNanoseconds));
    }
}
