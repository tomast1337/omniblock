using OmniBlock.Client.Rendering.Entities;
using OmniBlock.Entities.Behaviors;

namespace OmniBlock.Tests.Rendering;

public sealed class EntityRenderBaselineTests
{
    [Theory]
    [InlineData("cow")]
    [InlineData("sheep")]
    [InlineData("sheep_fur")]
    [InlineData("pig")]
    [InlineData("pig_saddle")]
    [InlineData("zombie")]
    [InlineData("creeper")]
    [InlineData("creeper_charged")]
    public void Every_fixture_model_dependency_resolves_including_renderer_aliases(string model)
    {
        using var source = File.OpenRead(EntityRenderBaseline.ModelAssetPath(model));
        Assert.True(source.Length > 0);
    }

    [Fact]
    public void Percentiles_use_nearest_rank_and_empty_is_unavailable_not_zero()
    {
        var summary = EntityRenderBaseline.Summarize(Enumerable.Range(1, 100).Select(x => (double)x));
        Assert.Equal(50, summary.P50);
        Assert.Equal(95, summary.P95);
        Assert.Equal(50.5, summary.Mean);
        Assert.Null(EntityRenderBaseline.Summarize([]).P50);
        Assert.Equal(3, EntityRenderBaseline.Summarize([3]).P95);
    }

    [Fact]
    public void Replicas_have_deterministic_positions_and_states_without_world_registration()
    {
        FakeWorldContext world = new();
        var before = world.Entities.Entities.Count;
        var first = EntityRenderBaseline.CreateEntities(world, "mixed", 64, 32, 0, 220, 0);
        var second = EntityRenderBaseline.CreateEntities(world, "mixed", 64, 32, 0, 220, 0);
        Assert.Equal(before, world.Entities.Entities.Count);
        Assert.Equal(64, first.Count);
        for (var i = 0; i < first.Count; i++)
        {
            var a = first[i]; var b = second[i];
            Assert.NotSame(a, b);
            Assert.Same(a.Type, b.Type);
            Assert.Equal((a.X, a.Y, a.Z, a.Yaw, a.Pitch), (b.X, b.Y, b.Z, b.Yaw, b.Pitch));
            Assert.Equal((a.X, a.Y, a.Z), (a.LastTickX, a.LastTickY, a.LastTickZ));
            Assert.Equal(0, a.Age);
            if (a.Behaviors.Find<WoolBehavior>() is { } wool)
            {
                Assert.Equal(i % 16, wool.ColorOf(a));
                Assert.Equal((i / 16) % 2 != 0, wool.IsShearedOn(a));
            }
        }
    }

    [Theory]
    [InlineData("cow", 257, 32)]
    [InlineData("cow", 0, 32)]
    [InlineData("empty", 1, 32)]
    [InlineData("unknown", 1, 32)]
    [InlineData("cow", 1, double.NaN)]
    [InlineData("cow", 1, 121)]
    public void Invalid_or_unbounded_fixtures_are_rejected(string scene, int count, double distance)
    {
        Assert.Throws<ArgumentException>(() =>
            EntityRenderBaseline.CreateEntities(new FakeWorldContext(), scene, count, distance, 0, 220, 0));
    }

    [Fact]
    public void World_metrics_sum_draws_but_do_not_count_previews_or_retain_previous_pass()
    {
        using (EntityPresentationMetrics.Begin())
        {
            EntityPresentationMetrics.Drew(10);
            EntityPresentationMetrics.Drew(3);
            EntityPresentationMetrics.Uploaded(120);
            EntityPresentationMetrics.Uploaded(36);
        }
        var sample = EntityPresentationMetrics.Last;
        Assert.Equal(13, sample.Instances);
        Assert.Equal(2, sample.DrawBatches);
        Assert.Equal(2, sample.InstanceUploads);
        Assert.Equal(156, sample.InstanceUploadBytes);
        EntityPresentationMetrics.Drew(100);
        Assert.Equal(sample, EntityPresentationMetrics.Last);
        using (EntityPresentationMetrics.Begin()) { }
        Assert.Equal(0, EntityPresentationMetrics.Last.Instances);
        Assert.Equal(0, EntityPresentationMetrics.Last.InstanceUploadBytes);
        Assert.Equal(sample.Serial + 1, EntityPresentationMetrics.Last.Serial);
    }
}
