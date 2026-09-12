using OmniBlock.Client.Rendering;
using OmniBlock.Client.Rendering.Particles;

namespace OmniBlock.Tests.Rendering;

public sealed class WorldPresentationPolicyTests
{
    [Theory]
    [InlineData(0, 32, 64, 48, 24, 1_000, 6)]
    [InlineData(1, 32, 128, 80, 40, 2_500, 8)]
    [InlineData(2, 32, 256, 128, 64, ParticleBuffer.MaxParticles, 10)]
    public void Quality_profiles_bound_non_terrain_work_independently_of_large_view_distance(
        int quality,
        int renderDistance,
        double entityDistance,
        double blockEntityDistance,
        double particleDistance,
        int particleLimit,
        int weatherRadius)
    {
        var policy = WorldPresentationPolicy.From(quality, renderDistance);

        Assert.Equal(entityDistance, policy.EntityDistance);
        Assert.Equal(blockEntityDistance, policy.BlockEntityDistance);
        Assert.Equal(particleDistance, policy.ParticleDistance);
        Assert.Equal(particleLimit, policy.MaxParticleInstances);
        Assert.Equal(weatherRadius, policy.WeatherRadius);
        Assert.Equal(1, policy.TranslucentSortIntervalFrames);
    }

    [Fact]
    public void Small_terrain_distance_clamps_entity_and_block_entity_ranges()
    {
        var policy = WorldPresentationPolicy.From(2, 4);

        Assert.Equal(64, policy.EntityDistance);
        Assert.Equal(64, policy.BlockEntityDistance);
        Assert.True(policy.ShouldRenderParticle(0, 0, 64));
        Assert.False(policy.ShouldRenderParticle(0, 0, 64.01));
    }
}
