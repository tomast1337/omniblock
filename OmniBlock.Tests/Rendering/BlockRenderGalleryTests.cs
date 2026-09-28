using OmniBlock.Client.Rendering.Blocks;

namespace OmniBlock.Tests.Rendering;

public sealed class BlockRenderGalleryTests
{
    [Fact]
    public void ProceduralFireCaptureUsesInstanceLocalRepeatableRandomness()
    {
        var first = new global::OmniBlock.Client.DynamicTexture.FireSprite("fire_layer_0", "unused")
            { RandomForTest = new Random(7419) };
        var second = new global::OmniBlock.Client.DynamicTexture.FireSprite("fire_layer_0", "unused")
            { RandomForTest = new Random(7419) };
        for (var tick = 0; tick < 64; tick++)
        {
            first.tick();
            _ = Random.Shared.Next(); // unrelated activity must not perturb the capture
            second.tick();
        }
        Assert.Equal(first.Pixels, second.Pixels);
        Assert.Contains(first.Pixels, value => value != 0);
    }

    [Fact]
    public void EveryCatalogBlockHasExactlyOneDefaultSampleAndNoDuplicateStates()
    {
        var blocks = ContentRuntime.Current.Blocks;
        var samples = BlockRenderGallery.BuildSamples(blocks);
        Assert.Equal(blocks.Keys.Select(key => key.ToString()).Order(),
            samples.Where(sample => sample.Meta == 0).Select(sample => sample.Id).Order());
        Assert.Equal(samples.Length, samples.Distinct().Count());
        Assert.All(samples, sample => Assert.InRange(sample.Meta, 0, 15));
        Assert.Equal(16, samples.Count(sample => sample.Id == "omniblock:wool"));
        Assert.Equal(4, samples.Count(sample => sample.Id == "omniblock:wooden_stairs"));
    }

    [Fact]
    public void PagePositionsAreDistinctAndInsideTheLoadedFixture()
    {
        var positions = Enumerable.Range(0, BlockRenderGallery.PageSize)
            .Select(BlockRenderGallery.Position).ToArray();
        Assert.Equal(positions.Length, positions.Distinct().Count());
        Assert.All(positions, pos =>
        {
            Assert.InRange(pos.X, 0, 15);
            Assert.InRange(pos.Z, 0, 15);
        });
    }
}
