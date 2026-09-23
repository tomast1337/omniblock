using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Worlds;

public sealed class TerrainLodSpatialSeamPlannerTests
{
    [Fact]
    public void Child_quartet_has_one_owned_segment_per_shared_edge()
    {
        var parent = new TerrainLodTileKey(1, 0, 0);
        var selected = Enumerable.Range(0, 4)
            .Select(index => Selection(parent.Child(index)))
            .ToArray();

        var seams = TerrainLodSpatialSeamPlanner.Plan(selected, includeExterior: false);

        Assert.Equal(4, seams.Count);
        Assert.All(seams, seam =>
        {
            Assert.False(seam.IsExterior);
            Assert.Equal(1, seam.LengthChunks);
        });
        Assert.Equal(2, seams.Count(static seam =>
            seam.OwnerSide == TerrainLodSpatialBoundarySide.East));
        Assert.Equal(2, seams.Count(static seam =>
            seam.OwnerSide == TerrainLodSpatialBoundarySide.South));

        var reversed = TerrainLodSpatialSeamPlanner.Plan(
            selected.Reverse(), includeExterior: false);
        Assert.Equal(seams, reversed);
    }

    [Fact]
    public void Coarse_edge_is_split_for_each_adjacent_refined_tile()
    {
        TerrainLodTileSelection[] selected =
        [
            Selection(new TerrainLodTileKey(1, 0, 0)),
            Selection(new TerrainLodTileKey(0, 2, 0)),
            Selection(new TerrainLodTileKey(0, 2, 1))
        ];

        var seams = TerrainLodSpatialSeamPlanner.Plan(selected, includeExterior: false);
        var coarseBoundary = seams.Where(static seam =>
            seam.Owner.Tile.Level == 1).ToArray();

        Assert.Collection(coarseBoundary,
            first => AssertSeam(first, fixedCoordinate: 2, alongStart: 0, alongEnd: 1),
            second => AssertSeam(second, fixedCoordinate: 2, alongStart: 1, alongEnd: 2));
        Assert.All(coarseBoundary, seam =>
        {
            Assert.Equal(new TerrainLodTileKey(1, 0, 0), seam.Owner.Tile);
            Assert.Equal(TerrainLodSpatialBoundarySide.East, seam.OwnerSide);
            Assert.Equal(0, seam.Neighbor!.Value.Tile.Level);
        });
    }

    [Fact]
    public void Exterior_is_partitioned_after_internal_coverage_in_negative_chunks()
    {
        TerrainLodTileSelection[] selected =
        [
            Selection(new TerrainLodTileKey(0, -2, -1)),
            Selection(new TerrainLodTileKey(0, -1, -1))
        ];

        var seams = TerrainLodSpatialSeamPlanner.Plan(selected);

        Assert.Equal(6, seams.Count(static seam => seam.IsExterior));
        var shared = Assert.Single(seams, static seam => !seam.IsExterior);
        AssertSeam(shared, fixedCoordinate: -1, alongStart: -1, alongEnd: 0);
        Assert.Equal(TerrainLodSpatialBoundarySide.East, shared.OwnerSide);
    }

    [Fact]
    public void Overlapping_parent_and_child_are_rejected()
    {
        TerrainLodTileSelection[] selected =
        [
            Selection(new TerrainLodTileKey(1, 0, 0)),
            Selection(new TerrainLodTileKey(0, 1, 1))
        ];

        var error = Assert.Throws<ArgumentException>(() =>
            TerrainLodSpatialSeamPlanner.Plan(selected));

        Assert.Contains("overlap", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static TerrainLodTileSelection Selection(TerrainLodTileKey key) =>
        new(key, HorizontalSampleLevel: Math.Min(key.Level, 1), MaximumVerticalSlices: 8);

    private static void AssertSeam(
        TerrainLodSpatialSeamSegment seam,
        long fixedCoordinate,
        long alongStart,
        long alongEnd)
    {
        Assert.Equal(fixedCoordinate, seam.FixedChunkCoordinate);
        Assert.Equal(alongStart, seam.AlongStartChunk);
        Assert.Equal(alongEnd, seam.AlongEndChunk);
    }
}
