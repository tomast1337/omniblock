using OmniBlock.Client.Rendering.Core.WebGPU;

namespace OmniBlock.Tests.Rendering;

public sealed class SharedQuadIndexBufferTests
{
    [Fact]
    public void FillIndices_builds_the_sequential_two_triangle_pattern()
    {
        Span<uint> indices = stackalloc uint[18];

        SharedQuadIndexBuffer.FillIndices(indices);

        Assert.Equal(
            new uint[]
            {
                0, 1, 2, 2, 3, 0,
                4, 5, 6, 6, 7, 4,
                8, 9, 10, 10, 11, 8
            },
            indices.ToArray());
    }

    [Fact]
    public void FillIndices_rejects_partial_quad_storage()
    {
        var indices = new uint[7];

        Assert.Throws<ArgumentException>(() => SharedQuadIndexBuffer.FillIndices(indices));
    }
}
