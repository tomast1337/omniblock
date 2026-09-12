using OmniBlock.Client.Rendering;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class ChunkDirtySectionRangeTests
{
    [Fact]
    public void Interior_block_and_its_neighbors_do_not_dirty_horizontal_neighbors()
    {
        var range = WorldRenderer.GetSectionRange(4, 63, 4, 6, 65, 6);

        Assert.Equal(new Vector3D<int>(0, 3, 0), range.Start);
        Assert.Equal(new Vector3D<int>(0, 4, 0), range.End);
    }

    [Fact]
    public void Boundary_neighbor_range_dirties_adjacent_sections()
    {
        var range = WorldRenderer.GetSectionRange(14, 14, 14, 16, 16, 16);

        Assert.Equal(new Vector3D<int>(0, 0, 0), range.Start);
        Assert.Equal(new Vector3D<int>(1, 1, 1), range.End);
    }

    [Fact]
    public void Negative_coordinates_use_floor_division()
    {
        var range = WorldRenderer.GetSectionRange(-7, 0, -17, -5, 0, -15);

        Assert.Equal(new Vector3D<int>(-1, 0, -2), range.Start);
        Assert.Equal(new Vector3D<int>(-1, 0, -1), range.End);
    }

    [Fact]
    public void Full_streamed_chunk_invalidates_resident_sections_on_both_boundaries()
    {
        var range = WorldRenderer.GetStreamingSectionRange(0, 0, 0, 15, 127, 15);

        Assert.Equal(new Vector3D<int>(-1, 0, -1), range.Start);
        Assert.Equal(new Vector3D<int>(1, 7, 1), range.End);
    }
}
