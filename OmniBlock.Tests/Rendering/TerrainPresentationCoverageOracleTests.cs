using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainPresentationCoverageOracleTests
{
    [Fact]
    public void Exact_column_and_spatial_owners_form_one_complete_footprint()
    {
        var report = Evaluate([
            new(0, 0, ExactPresent: true, ColumnLodPresent: false, 0, false),
            new(1, 0, ExactPresent: false, ColumnLodPresent: true, 0, false),
            new(2, 0, ExactPresent: false, ColumnLodPresent: true, 0, true)
        ]);

        Assert.True(report.IsComplete);
        Assert.Equal(3, report.CoveredColumns);
        Assert.Equal(1, report.ExactOwnedColumns);
        Assert.Equal(1, report.ColumnLodOwnedColumns);
        Assert.Equal(1, report.SpatialOwnedColumns);
    }

    [Fact]
    public void Complementary_handoff_is_one_owner_not_an_overlap()
    {
        var report = Evaluate([
            new(7, -4, ExactPresent: true, ColumnLodPresent: true, 0.5f, false)
        ]);

        Assert.True(report.IsComplete);
        Assert.Equal(1, report.TransitionColumns);
        Assert.Equal(0, report.OverlapCount);
    }

    [Fact]
    public void Handoff_cannot_advance_without_an_exact_replacement()
    {
        var report = Evaluate([
            new(7, -4, ExactPresent: false, ColumnLodPresent: true, 0.5f, false)
        ]);

        Assert.False(report.IsComplete);
        Assert.Equal(1, report.HoleCount);
        Assert.Equal(TerrainCoverageFailureKind.InvalidHandoff, report.FirstFailureKind);
        Assert.Equal(7, report.FirstFailureX);
        Assert.Equal(-4, report.FirstFailureZ);
    }

    [Fact]
    public void Missing_owner_reports_the_responsible_coordinate()
    {
        var report = Evaluate([
            new(-9, 12, ExactPresent: false, ColumnLodPresent: false, 0, false)
        ]);

        Assert.Equal(TerrainCoverageFailureKind.MissingOwner, report.FirstFailureKind);
        Assert.Equal(-9, report.FirstFailureX);
        Assert.Equal(12, report.FirstFailureZ);
        Assert.Equal(1, report.MissingExactMeshColumns);
    }

    [Fact]
    public void Exact_footprint_includes_columns_with_no_loaded_chunk_or_mesh()
    {
        HashSet<(int X, int Z)> footprint = [];
        TerrainPresentationCoverageOracle.AddExactRadiusColumns(footprint, 0.5, 0.5, 2);

        Assert.Contains((0, 0), footprint);
        Assert.Contains((1, 0), footprint);
        Assert.Contains((-1, 0), footprint);
        Assert.DoesNotContain((2, 0), footprint);
        var report = Evaluate(footprint.Select(column => new TerrainCoverageColumn(
            column.X, column.Z, false, false, 0, false,
            ChunkDataLoaded: false)).ToArray());
        Assert.Equal(footprint.Count, report.ExpectedColumns);
        Assert.Equal(footprint.Count, report.MissingChunkDataColumns);
        Assert.Equal(footprint.Count, report.HoleCount);
    }

    [Fact]
    public void Missing_coverage_distinguishes_data_mesh_and_presentation()
    {
        var report = Evaluate([
            new(0, 0, false, false, 0, false, ChunkDataLoaded: false),
            new(1, 0, false, false, 0, false),
            new(2, 0, true, true, 0.5f, false),
            new(3, 0, false, true, 0.5f, false)
        ]);

        Assert.Equal(3, report.HoleCount);
        Assert.Equal(1, report.MissingChunkDataColumns);
        Assert.Equal(2, report.MissingExactMeshColumns);
        Assert.Equal(0, report.MissingPresentationColumns);
    }

    [Fact]
    public void Spatial_cover_is_valid_without_an_exact_chunk()
    {
        var report = Evaluate([
            new(4, -3, false, false, 0, true, ChunkDataLoaded: false)
        ]);

        Assert.True(report.IsComplete);
        Assert.Equal(0, report.MissingChunkDataColumns);
        Assert.Equal(1, report.SpatialOwnedColumns);
    }

    [Fact]
    public void Spatial_body_conflict_is_an_overlap_even_when_the_column_is_covered()
    {
        var report = Evaluate([
            new(2, 3, ExactPresent: false, ColumnLodPresent: false, 0, true,
                SpatialBodyConflict: true)
        ]);

        Assert.Equal(1, report.CoveredColumns);
        Assert.Equal(1, report.OverlapCount);
        Assert.Equal(TerrainCoverageFailureKind.OverlappingSpatialBodies,
            report.FirstFailureKind);
    }

    [Fact]
    public void Spatial_seams_must_match_the_published_body_snapshot()
    {
        var selection = new TerrainLodTileSelection(new(2, 1, -1), 1, 16);
        var expected = TerrainLodSpatialSeamPlanner.Plan([selection]);
        var report = TerrainPresentationCoverageOracle.Evaluate(
            [new(4, -4, false, false, 0, true)],
            expectedColumnSeams: 0,
            missingColumnSeams: 0,
            pendingColumnSeams: 0,
            expected,
            expected.Skip(1));

        Assert.False(report.IsComplete);
        Assert.Equal(1, report.MissingSeams);
        Assert.Equal(TerrainCoverageFailureKind.MissingSpatialSeam,
            report.FirstFailureKind);
    }

    [Fact]
    public void Pending_seam_replacement_is_complete_when_a_fallback_still_covers_the_edge()
    {
        var report = TerrainPresentationCoverageOracle.Evaluate(
            [new(0, 0, true, false, 0, false)],
            expectedColumnSeams: 2,
            missingColumnSeams: 0,
            pendingColumnSeams: 2,
            Array.Empty<TerrainLodSpatialSeamSegment>(),
            Array.Empty<TerrainLodSpatialSeamSegment>());

        Assert.True(report.IsComplete);
        Assert.Equal(2, report.PendingReplacementSeams);
        Assert.Equal(0, report.MissingSeams);
    }

    [Fact]
    public void Missing_column_seam_reports_its_owner_coordinate()
    {
        var report = TerrainPresentationCoverageOracle.Evaluate(
            [new(0, 0, true, false, 0, false)],
            expectedColumnSeams: 1,
            missingColumnSeams: 1,
            pendingColumnSeams: 1,
            Array.Empty<TerrainLodSpatialSeamSegment>(),
            Array.Empty<TerrainLodSpatialSeamSegment>(),
            firstMissingColumnSeam: (-17, 22));

        Assert.False(report.IsComplete);
        Assert.Equal(TerrainCoverageFailureKind.MissingColumnSeam,
            report.FirstFailureKind);
        Assert.Equal(-17, report.FirstFailureX);
        Assert.Equal(22, report.FirstFailureZ);
    }

    private static TerrainCoverageSnapshot Evaluate(TerrainCoverageColumn[] columns) =>
        TerrainPresentationCoverageOracle.Evaluate(
            columns, 0, 0, 0,
            Array.Empty<TerrainLodSpatialSeamSegment>(),
            Array.Empty<TerrainLodSpatialSeamSegment>());
}
