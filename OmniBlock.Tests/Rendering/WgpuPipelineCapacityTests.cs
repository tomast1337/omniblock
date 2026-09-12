using OmniBlock.Client.Rendering.Core.WebGPU;

namespace OmniBlock.Tests.Rendering;

public sealed class WgpuPipelineCapacityTests
{
    [Theory]
    [InlineData(0, 1, 256)]
    [InlineData(256, 257, 512)]
    [InlineData(512, 900, 1024)]
    [InlineData(32768, 65536, 65536)]
    public void Dynamic_uniform_arena_grows_geometrically_with_a_fixed_bound(
        int current,
        int required,
        int expected) =>
        Assert.Equal(expected, WgpuPipeline.NextDynamicUniformCapacity(current, required));

    [Fact]
    public void Dynamic_uniform_arena_rejects_an_unbounded_batch() =>
        Assert.Throws<InvalidOperationException>(() =>
            WgpuPipeline.NextDynamicUniformCapacity(
                WgpuPipeline.MaxDynamicUniformEntries,
                WgpuPipeline.MaxDynamicUniformEntries + 1));
}
