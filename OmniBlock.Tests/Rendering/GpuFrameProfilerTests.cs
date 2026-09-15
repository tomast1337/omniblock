using OmniBlock.Client.Rendering.Core.WebGPU;

namespace OmniBlock.Tests.Rendering;

public sealed class GpuFrameProfilerTests
{
    [Fact]
    public void Selects_timestamp_period_only_for_one_matching_vulkan_adapter()
    {
        VulkanTimestampPeriodResolver.Candidate[] candidates =
        [
            new(0x1002, 0x744c, 1.0f),
            new(0x10de, 0x2684, 0.5f)
        ];

        var period = VulkanTimestampPeriodResolver.SelectUnique(
            0x10de, 0x2684, candidates, out var matches);

        Assert.Equal(1, matches);
        Assert.Equal(0.5, period);
    }

    [Fact]
    public void Rejects_ambiguous_or_invalid_vulkan_adapter_periods()
    {
        VulkanTimestampPeriodResolver.Candidate[] ambiguous =
        [
            new(0x1002, 0x744c, 1.0f),
            new(0x1002, 0x744c, 2.0f),
            new(0x1002, 0x744c, 0.0f)
        ];

        var period = VulkanTimestampPeriodResolver.SelectUnique(
            0x1002, 0x744c, ambiguous, out var matches);

        Assert.Equal(2, matches);
        Assert.Null(period);
    }

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
        Assert.Equal(2, snapshot.World.PhysicalPasses);
        Assert.Equal(0.0002, snapshot.World.Milliseconds);
        Assert.Equal(30UL, snapshot.Interface.RawTicks);
        Assert.Equal(40UL, snapshot.Composite.RawTicks);
        Assert.Equal(0UL, snapshot.FirstPersonHand.RawTicks);
        Assert.Equal(0, snapshot.FirstPersonHand.PhysicalPasses);
    }

    [Fact]
    public void Explicit_encoder_span_includes_work_between_physical_render_passes()
    {
        ulong[] values =
        [
            80, 400, // encoder-level frame timestamps
            100, 150,
            300, 350
        ];
        GpuFrameProfiler.QueryRange[] ranges =
        [
            new(GpuPassCategory.World, 2, 3),
            new(GpuPassCategory.Composite, 4, 5)
        ];

        var snapshot = GpuFrameProfiler.BuildSnapshot(
            9, "available", 1.0, values, ranges, 0, 1, 1920, 1080);

        Assert.Equal(320UL, snapshot.RenderSpanRawTicks);
        Assert.Equal((uint)1920, snapshot.Width);
        Assert.Equal((uint)1080, snapshot.Height);
        Assert.Equal(50UL, snapshot.World.RawTicks);
        Assert.Equal(50UL, snapshot.Composite.RawTicks);
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
