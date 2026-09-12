using OmniBlock.Client;
using OmniBlock.Client.Options;

namespace OmniBlock.Tests.Rendering;

public sealed class FramePacerTests
{
    [Theory]
    [InlineData(0f, 30)]
    [InlineData(1f / 7f, 60)]
    [InlineData(3f / 7f, 120)]
    public void Slider_value_maps_to_its_frame_limit(float normalized, int expected)
    {
        Assert.Equal(expected, GameOptions.DecodeFrameRateLimit(normalized));
    }

    [Fact]
    public void Slider_maximum_is_unlimited()
    {
        Assert.Null(GameOptions.DecodeFrameRateLimit(1f));
    }

    [Theory]
    [InlineData(0, 60, 1_000, 17)]
    [InlineData(10_000, 30, 1_000, 10_034)]
    [InlineData(0, 120, 10_000_000, 83_334)]
    public void Target_timestamp_rounds_up_without_shortening_the_frame(
        long startedAt,
        int fps,
        long frequency,
        long expected)
    {
        Assert.Equal(expected, FramePacer.TargetTimestamp(startedAt, fps, frequency));
    }
}
