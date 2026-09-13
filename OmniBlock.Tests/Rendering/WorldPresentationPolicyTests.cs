using OmniBlock.Client.Rendering;
using OmniBlock.Client.Rendering.Particles;
using OmniBlock.Entities;
using OmniBlock.Util.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class WorldPresentationPolicyTests
{
    [Theory]
    [InlineData(0, 32, 96, 48, 24, 1_000, 6)]
    [InlineData(1, 32, 512, 80, 40, 2_500, 8)]
    [InlineData(2, 32, 512, 128, 64, ParticleBuffer.MaxParticles, 10)]
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

    [Fact]
    public void Balanced_policy_replaces_the_legacy_64_block_mob_limit()
    {
        FakeWorldContext world = new();
        var cow = (EntityCreature)TestEntityCatalog.ByName("cow").Create(world);
        cow.SetPosition(0, 64, 0);
        var cameraAt120Blocks = new Vec3D(120, 64, 0);
        var cameraAt400Blocks = new Vec3D(400, 64, 0);
        var cameraBeyondPolicy = new Vec3D(513, 64, 0);
        var policy = WorldPresentationPolicy.From(1, 32);

        Assert.False(cow.ShouldRender(cameraAt120Blocks));
        Assert.True(policy.ShouldRenderEntity(cow, cameraAt120Blocks));
        Assert.True(policy.ShouldRenderEntity(cow, cameraAt400Blocks));
        Assert.False(policy.ShouldRenderEntity(cow, cameraBeyondPolicy));
    }
}
