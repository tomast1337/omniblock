using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Client.Rendering.Chunks.Occlusion;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class ChunkDirectionalRangesTests
{
    [Fact]
    public void Selection_rejects_hidden_buckets_keeps_unknown_and_coalesces_adjacent_ranges()
    {
        ChunkDirectionalRanges ranges = new(
            new ChunkQuadRange(0, 2),
            new ChunkQuadRange(2, 1),
            new ChunkQuadRange(3, 0),
            new ChunkQuadRange(3, 2),
            new ChunkQuadRange(5, 1),
            new ChunkQuadRange(6, 1),
            new ChunkQuadRange(7, 3));
        Span<ChunkQuadRange> selected = stackalloc ChunkQuadRange[7];

        var count = ranges.Select(
            ChunkDirectionMask.Down | ChunkDirectionMask.Up | ChunkDirectionMask.South,
            selected);

        Assert.Equal(2, count);
        Assert.Equal(new ChunkQuadRange(0, 5), selected[0]);
        Assert.Equal(new ChunkQuadRange(7, 3), selected[1]);
    }

    [Fact]
    public void Camera_outside_page_selects_one_face_per_axis()
    {
        var mask = DirectionalFaceVisibility.ForPage(
            new Vector3D<int>(16, 32, 48),
            1,
            new Vector3D<double>(0, 100, 80));

        Assert.Equal(
            ChunkDirectionMask.West | ChunkDirectionMask.Up | ChunkDirectionMask.South,
            mask);
    }

    [Fact]
    public void Camera_inside_axis_slab_keeps_both_directions_conservatively()
    {
        var mask = DirectionalFaceVisibility.ForPage(
            new Vector3D<int>(16, 32, 48),
            1,
            new Vector3D<double>(20, 38, 50));

        Assert.Equal(ChunkDirectionMask.All, mask);
    }
}
