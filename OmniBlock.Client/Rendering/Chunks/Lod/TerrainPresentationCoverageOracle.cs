using OmniBlock.Worlds.Lod;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

internal enum TerrainCoverageFailureKind
{
    None,
    MissingOwner,
    InvalidHandoff,
    OverlappingSpatialBodies,
    MissingColumnSeam,
    MissingSpatialSeam,
    UnexpectedSpatialSeam
}

internal readonly record struct TerrainCoverageColumn(
    int X,
    int Z,
    bool ExactPresent,
    bool ColumnLodPresent,
    float ColumnLodHandoff,
    bool SpatialAuthoritative,
    bool SpatialBodyConflict = false);

internal readonly record struct TerrainCoverageSnapshot(
    int ExpectedColumns,
    int CoveredColumns,
    int ExactOwnedColumns,
    int ColumnLodOwnedColumns,
    int SpatialOwnedColumns,
    int TransitionColumns,
    int HoleCount,
    int OverlapCount,
    int ExpectedSeams,
    int MissingSeams,
    int PendingReplacementSeams,
    int UnexpectedSeams,
    TerrainCoverageFailureKind FirstFailureKind,
    int FirstFailureX,
    int FirstFailureZ)
{
    public bool IsComplete =>
        ExpectedColumns > 0 && HoleCount == 0 && OverlapCount == 0 &&
        MissingSeams == 0 && UnexpectedSeams == 0;
}

/// <summary>
///     Resolves the logical presentation owner of every column in the current camera footprint.
///     Retained GPU resources may overlap, but their display masks must resolve to one owner or one
///     complementary exact/LOD handoff. Seam membership is checked against the same published body
///     snapshot rather than against a candidate partition still being compiled.
/// </summary>
internal static class TerrainPresentationCoverageOracle
{
    public static TerrainCoverageSnapshot Evaluate(
        IEnumerable<TerrainCoverageColumn> columns,
        int expectedColumnSeams,
        int missingColumnSeams,
        int pendingColumnSeams,
        IEnumerable<TerrainLodSpatialSeamSegment> expectedSpatialSeams,
        IEnumerable<TerrainLodSpatialSeamSegment> publishedSpatialSeams,
        (int X, int Z)? firstMissingColumnSeam = null)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(expectedSpatialSeams);
        ArgumentNullException.ThrowIfNull(publishedSpatialSeams);

        var expected = 0;
        var covered = 0;
        var exact = 0;
        var columnLod = 0;
        var spatial = 0;
        var transitions = 0;
        var holes = 0;
        var overlaps = 0;
        var firstKind = TerrainCoverageFailureKind.None;
        var firstX = 0;
        var firstZ = 0;

        foreach (var column in columns)
        {
            expected++;
            if (column.SpatialBodyConflict)
            {
                overlaps++;
                RecordFailure(TerrainCoverageFailureKind.OverlappingSpatialBodies,
                    column.X, column.Z);
            }

            if (column.SpatialAuthoritative)
            {
                spatial++;
                covered++;
                continue;
            }

            if (column.ColumnLodPresent)
            {
                var progress = Math.Clamp(column.ColumnLodHandoff, 0, 1);
                if (progress <= 0)
                {
                    columnLod++;
                    covered++;
                    continue;
                }

                if (!column.ExactPresent)
                {
                    holes++;
                    RecordFailure(TerrainCoverageFailureKind.InvalidHandoff,
                        column.X, column.Z);
                    continue;
                }

                covered++;
                if (progress < 1)
                {
                    transitions++;
                    continue;
                }

                exact++;
                continue;
            }

            if (column.ExactPresent)
            {
                exact++;
                covered++;
                continue;
            }

            holes++;
            RecordFailure(TerrainCoverageFailureKind.MissingOwner, column.X, column.Z);
        }

        var expectedSpatial = expectedSpatialSeams.ToHashSet();
        var publishedSpatial = publishedSpatialSeams.ToHashSet();
        var missingSpatial = expectedSpatial.Count(seam => !publishedSpatial.Contains(seam));
        var unexpectedSpatial = publishedSpatial.Count(seam => !expectedSpatial.Contains(seam));
        var missingSeams = Math.Max(0, missingColumnSeams) + missingSpatial;
        var expectedSeams = Math.Max(0, expectedColumnSeams) + expectedSpatial.Count;
        if (missingColumnSeams > 0)
            RecordFailure(TerrainCoverageFailureKind.MissingColumnSeam,
                firstMissingColumnSeam?.X ?? 0, firstMissingColumnSeam?.Z ?? 0);
        if (missingSpatial > 0)
        {
            var seam = expectedSpatial.First(candidate => !publishedSpatial.Contains(candidate));
            RecordFailure(TerrainCoverageFailureKind.MissingSpatialSeam,
                checked((int)seam.Owner.Tile.MinChunkX),
                checked((int)seam.Owner.Tile.MinChunkZ));
        }
        if (unexpectedSpatial > 0)
        {
            var seam = publishedSpatial.First(candidate => !expectedSpatial.Contains(candidate));
            RecordFailure(TerrainCoverageFailureKind.UnexpectedSpatialSeam,
                checked((int)seam.Owner.Tile.MinChunkX),
                checked((int)seam.Owner.Tile.MinChunkZ));
        }

        return new TerrainCoverageSnapshot(
            expected, covered, exact, columnLod, spatial, transitions,
            holes, overlaps, expectedSeams, missingSeams,
            Math.Max(0, pendingColumnSeams), unexpectedSpatial,
            firstKind, firstX, firstZ);

        void RecordFailure(TerrainCoverageFailureKind kind, int x, int z)
        {
            if (firstKind != TerrainCoverageFailureKind.None) return;
            firstKind = kind;
            firstX = x;
            firstZ = z;
        }
    }
}
