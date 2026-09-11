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
            new uint[] { 0, 1, 2, 2, 3, 0, 4, 5, 6, 6, 7, 4, 8, 9, 10, 10, 11, 8 },
            indices.ToArray());
    }

    [Fact]
    public void FillIndices_rejects_partial_quad_storage()
    {
        var indices = new uint[7];

        Assert.Throws<ArgumentException>(() => SharedQuadIndexBuffer.FillIndices(indices));
    }

    [Fact]
    public void Wireframe_indices_reuse_quad_vertices_for_both_triangle_edges()
    {
        Span<uint> indices = stackalloc uint[24];

        SharedQuadWireframeIndexBuffer.FillIndices(indices);

        Assert.Equal(
            new uint[]
            {
                0, 1, 1, 2, 2, 0, 2, 3, 3, 0, 0, 2,
                4, 5, 5, 6, 6, 4, 6, 7, 7, 4, 4, 6
            },
            indices.ToArray());
    }

    [Fact]
    public void Wireframe_indices_reject_partial_quad_storage()
    {
        Assert.Throws<ArgumentException>(() =>
            SharedQuadWireframeIndexBuffer.FillIndices(new uint[13]));
    }
}
