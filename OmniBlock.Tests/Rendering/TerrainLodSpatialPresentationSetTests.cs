using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodSpatialPresentationSetTests
{
    private static readonly TerrainLodSpatialPolicy RefiningPolicy = new(
        distanceUnitChunks: 1000,
        distanceGrowth: 2,
        horizontalSampleLevelBySpatialLevel: [0, 0],
        verticalSliceBudgetBySpatialLevel: [16, 8]);

    [Fact]
    public void Revision_advances_only_when_a_new_presentation_is_published()
    {
        var key = new TerrainLodTileKey(0, 0, 0);
        using var presentations = new TerrainLodSpatialPresentationSet<FakePresentation>();

        Assert.Equal(0, presentations.Revision);
        Assert.True(presentations.TryInstall(
            key, "first", () => new FakePresentation("first"), out _));
        Assert.Equal(1, presentations.Revision);
        Assert.False(presentations.TryInstall(
            key, "first", () => new FakePresentation("unused"), out _));
        Assert.Equal(1, presentations.Revision);
        Assert.False(presentations.TryInstall(
            key, "second", static () => throw new InvalidOperationException("failed"), out _));
        Assert.Equal(1, presentations.Revision);
        Assert.True(presentations.TryInstall(
            key, "second", () => new FakePresentation("second"), out _));
        Assert.Equal(2, presentations.Revision);
    }

    [Fact]
    public void Failed_candidate_keeps_the_published_predecessor()
    {
        var key = new TerrainLodTileKey(0, 2, -3);
        using var presentations = new TerrainLodSpatialPresentationSet<FakePresentation>();
        var predecessor = new FakePresentation("old");
        Assert.True(presentations.TryInstall(key, "old", () => predecessor, out var firstFailure));
        Assert.Null(firstFailure);

        Assert.False(presentations.TryInstall(
            key, "new", static () => throw new InvalidOperationException("upload failed"),
            out var failure));
        var frame = presentations.Update(
            key, 2, -3, new TerrainLodSpatialPolicy(8, 2, [0], [16]),
            1, fadeEnabled: false);

        Assert.IsType<InvalidOperationException>(failure);
        Assert.Single(frame.Draws);
        Assert.Same(predecessor, frame.Draws[0].Presentation);
        Assert.False(predecessor.Disposed);
    }

    [Fact]
    public void Successful_candidate_is_published_before_predecessor_retires()
    {
        var key = new TerrainLodTileKey(0, 0, 0);
        using var presentations = new TerrainLodSpatialPresentationSet<FakePresentation>();
        var old = new FakePresentation("old");
        var replacement = new FakePresentation("replacement");
        Assert.True(presentations.TryInstall(key, "old", () => old, out _));

        Assert.True(presentations.TryInstall(key, "replacement", () => replacement, out var failure));
        var frame = presentations.Update(
            key, 0, 0, new TerrainLodSpatialPolicy(8, 2, [0], [16]),
            1, fadeEnabled: false);

        Assert.Null(failure);
        Assert.True(old.Disposed);
        Assert.False(replacement.Disposed);
        Assert.Same(replacement, Assert.Single(frame.Draws).Presentation);
    }

    [Fact]
    public void Unpinned_catalog_entry_can_be_evicted_and_releases_its_resource()
    {
        var key = new TerrainLodTileKey(0, 9, -4);
        using var presentations = new TerrainLodSpatialPresentationSet<FakePresentation>();
        var presentation = new FakePresentation("trailing");
        Assert.True(presentations.TryInstall(key, "trailing", () => presentation, out _));

        Assert.True(presentations.TryEvict(key));

        Assert.True(presentation.Disposed);
        Assert.False(presentations.IsReady(key));
        Assert.Equal(0, presentations.Count);
        Assert.Equal(2, presentations.Revision);
    }

    [Fact]
    public void Active_transition_members_cannot_be_evicted()
    {
        var root = new TerrainLodTileKey(1, 0, 0);
        using var presentations = new TerrainLodSpatialPresentationSet<FakePresentation>();
        Install(presentations, root);
        presentations.UpdatePartition(
            root, [Selection(root)], completeRootCoverage: true,
            deltaTime: 0, fadeEnabled: false);

        Assert.Contains(root, presentations.TransitionTiles);
        Assert.False(presentations.TryEvict(root));
        Assert.True(presentations.IsReady(root));
    }

    [Fact]
    public void Partial_child_quartet_cannot_replace_ready_parent()
    {
        var root = new TerrainLodTileKey(1, 0, 0);
        using var presentations = new TerrainLodSpatialPresentationSet<FakePresentation>();
        Install(presentations, root);
        for (var index = 0; index < 3; index++) Install(presentations, root.Child(index));

        var frame = presentations.Update(
            root, 1, 1, RefiningPolicy, 1, fadeEnabled: true);

        Assert.True(frame.CompleteCoverage);
        Assert.False(frame.Transitioning);
        Assert.Equal(root, Assert.Single(frame.Draws).Selection.Tile);
    }

    [Fact]
    public void Complete_child_quartet_fades_as_one_partition()
    {
        var root = new TerrainLodTileKey(1, 0, 0);
        using var presentations = new TerrainLodSpatialPresentationSet<FakePresentation>();
        Install(presentations, root);
        var initial = presentations.Update(
            root, 1, 1, RefiningPolicy, 0, fadeEnabled: true);
        Assert.Equal(root, Assert.Single(initial.Draws).Selection.Tile);

        for (var index = 0; index < 4; index++) Install(presentations, root.Child(index));
        var transition = presentations.Update(
            root, 1, 1, RefiningPolicy,
            TerrainLodSpatialPresentationSet<FakePresentation>.TransitionDurationSeconds / 2,
            fadeEnabled: true);

        Assert.True(transition.CompleteCoverage);
        Assert.True(transition.Transitioning);
        Assert.Equal(5, transition.Draws.Count);
        Assert.Equal(root, transition.Draws[0].Selection.Tile);
        Assert.Equal(2u, transition.Draws[0].Fade.Mode);
        Assert.All(transition.Draws.Skip(1), draw =>
        {
            Assert.Equal(0, draw.Selection.Tile.Level);
            Assert.Equal(1u, draw.Fade.Mode);
            Assert.Equal(transition.Draws[0].Fade.Progress, draw.Fade.Progress);
            Assert.Equal(transition.Draws[0].Fade.Seed, draw.Fade.Seed);
        });

        var completed = presentations.Update(
            root, 1, 1, RefiningPolicy,
            TerrainLodSpatialPresentationSet<FakePresentation>.TransitionDurationSeconds,
            fadeEnabled: true);
        Assert.False(completed.Transitioning);
        Assert.Equal(4, completed.Draws.Count);
        Assert.DoesNotContain(completed.Draws, draw => draw.Selection.Tile == root);
        Assert.All(completed.Draws, draw => Assert.Equal(0u, draw.Fade.Mode));
    }

    [Fact]
    public void Independent_roots_keep_independent_transition_state()
    {
        var first = new TerrainLodTileKey(1, 0, 0);
        var second = new TerrainLodTileKey(1, 1, 0);
        using var presentations = new TerrainLodSpatialPresentationSet<FakePresentation>();
        Install(presentations, first);
        Install(presentations, second);
        presentations.Update(first, 1, 1, RefiningPolicy, 0, fadeEnabled: true);
        presentations.Update(second, 3, 1, RefiningPolicy, 0, fadeEnabled: true);
        for (var index = 0; index < 4; index++) Install(presentations, first.Child(index));

        var firstFrame = presentations.Update(
            first, 1, 1, RefiningPolicy,
            TerrainLodSpatialPresentationSet<FakePresentation>.TransitionDurationSeconds / 2,
            fadeEnabled: true);
        var secondFrame = presentations.Update(
            second, 3, 1, RefiningPolicy,
            TerrainLodSpatialPresentationSet<FakePresentation>.TransitionDurationSeconds / 2,
            fadeEnabled: true);

        Assert.True(firstFrame.Transitioning);
        Assert.False(secondFrame.Transitioning);
        Assert.Equal(second, Assert.Single(secondFrame.Draws).Selection.Tile);
    }

    [Fact]
    public void Partial_management_root_grows_without_fading_uncovered_space()
    {
        var managementRoot = new TerrainLodTileKey(2, 0, 0);
        var first = managementRoot.Child(0);
        var second = managementRoot.Child(1);
        using var presentations = new TerrainLodSpatialPresentationSet<FakePresentation>();
        Install(presentations, first);
        Install(presentations, second);

        var initial = presentations.UpdatePartition(
            managementRoot,
            [Selection(first)],
            completeRootCoverage: false,
            deltaTime: 0,
            fadeEnabled: true);
        var grown = presentations.UpdatePartition(
            managementRoot,
            [Selection(first), Selection(second)],
            completeRootCoverage: false,
            deltaTime: TerrainLodSpatialPresentationSet<FakePresentation>
                .TransitionDurationSeconds / 2,
            fadeEnabled: true);

        Assert.False(initial.Transitioning);
        Assert.False(grown.Transitioning);
        Assert.Equal([first, second], grown.Draws.Select(static draw => draw.Selection.Tile));
        Assert.All(grown.Draws, static draw => Assert.Equal(0u, draw.Fade.Mode));
    }

    [Fact]
    public void Complete_forest_partition_promotes_to_parent_as_one_group()
    {
        var root = new TerrainLodTileKey(1, 0, 0);
        using var presentations = new TerrainLodSpatialPresentationSet<FakePresentation>();
        var children = Enumerable.Range(0, 4).Select(root.Child).ToArray();
        foreach (var child in children) Install(presentations, child);
        Install(presentations, root);
        presentations.UpdatePartition(
            root,
            children.Select(Selection).ToArray(),
            completeRootCoverage: true,
            deltaTime: 0,
            fadeEnabled: true);

        var transition = presentations.UpdatePartition(
            root,
            [Selection(root)],
            completeRootCoverage: true,
            deltaTime: TerrainLodSpatialPresentationSet<FakePresentation>
                .TransitionDurationSeconds / 2,
            fadeEnabled: true);

        Assert.True(transition.Transitioning);
        Assert.Equal(5, transition.Draws.Count);
        Assert.Equal(4, transition.Draws.Count(static draw => draw.Fade.Mode == 2));
        Assert.Single(transition.Draws, static draw => draw.Fade.Mode == 1);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(-1, -4)]
    public void Mixed_tile_retains_columns_beyond_the_bounded_handoff_window(int tileX, int tileZ)
    {
        var tile = new TerrainLodTileKey(3, tileX, tileZ);
        HashSet<TerrainLodTileKey> published = [tile];
        var minX = (int)tile.MinChunkX;
        var minZ = (int)tile.MinChunkZ;
        HashSet<(int X, int Z)> replacements = [(minX, minZ)];
        for (var z = minZ; z <= tile.MaxChunkZ; z++)
        for (var x = minX; x <= tile.MaxChunkX; x++)
            Assert.Equal((x, z) != (minX, minZ),
                TerrainLodSpatialAuthority.OwnsColumn(x, z, published, replacements, 2, 7));
        Assert.False(TerrainLodSpatialAuthority.OwnsColumn(
            minX - 1, minZ, published, replacements, 2, 7));
        replacements.Clear();
        Assert.True(TerrainLodSpatialAuthority.OwnsColumn(
            minX, minZ, published, replacements, 2, 7));
    }

    [Fact]
    public void Tile_intersecting_near_guard_band_needs_per_column_replacement_checks()
    {
        var tile = new TerrainLodTileKey(2, 1, 0); // chunks 4..7

        Assert.False(TerrainLodSpatialAuthority.IsBeyondNearRadius(
            tile, cameraChunkX: 0, cameraChunkZ: 0, renderDistance: 4));
    }

    [Fact]
    public void Entirely_distant_tile_can_replace_legacy_column_lod()
    {
        var tile = new TerrainLodTileKey(2, 2, 0); // chunks 8..11

        Assert.True(TerrainLodSpatialAuthority.IsBeyondNearRadius(
            tile, cameraChunkX: 0, cameraChunkZ: 0, renderDistance: 4));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(-1, -1)]
    public void Approaching_a_tile_keeps_coverage_until_every_replacement_column_is_ready(int x, int z)
    {
        var tile = new TerrainLodTileKey(2, x, z);
        HashSet<(int X, int Z)> ready = [];
        bool ReplacementReady(int cx, int cz) => ready.Contains((cx, cz));
        var cameraX = (double)tile.MinChunkX;
        var cameraZ = (double)tile.MinChunkZ;

        Assert.False(TerrainLodSpatialAuthority.IsBeyondNearRadius(tile, cameraX, cameraZ, 4));
        Assert.True(TerrainLodSpatialAuthority.ShouldPresent(tile, cameraX, cameraZ, 4, ReplacementReady));
        for (var cz = tile.MinChunkZ; cz <= tile.MaxChunkZ; cz++)
        for (var cx = tile.MinChunkX; cx <= tile.MaxChunkX; cx++)
        {
            // Even the last missing column must keep the complete coarse tile drawable.
            Assert.True(TerrainLodSpatialAuthority.ShouldPresent(tile, cameraX, cameraZ, 4, ReplacementReady));
            ready.Add(((int)cx, (int)cz));
        }
        Assert.False(TerrainLodSpatialAuthority.ShouldPresent(tile, cameraX, cameraZ, 4, ReplacementReady));

        ready.Remove(((int)tile.MinChunkX, (int)tile.MinChunkZ));
        Assert.True(TerrainLodSpatialAuthority.ShouldPresent(tile, cameraX, cameraZ, 4, ReplacementReady));
    }

    [Fact]
    public void Growing_exact_radius_keeps_the_published_spatial_annulus_until_replacements_are_ready()
    {
        var caveTile = new TerrainLodTileKey(2, 0, 0);
        HashSet<(int X, int Z)> ready = [];
        bool ExactReady(int x, int z) => ready.Contains((x, z));
        const double cameraX = 1.5;
        const double cameraZ = -7.5;

        Assert.Equal(4, TerrainLodSpatialNearDistance.Resolve(
            4, 14, cameraX, cameraZ, [caveTile], ExactReady));
        for (var z = caveTile.MinChunkZ; z <= caveTile.MaxChunkZ; z++)
        for (var x = caveTile.MinChunkX; x <= caveTile.MaxChunkX; x++)
        {
            Assert.Equal(4, TerrainLodSpatialNearDistance.Resolve(
                4, 14, cameraX, cameraZ, [caveTile], ExactReady));
            ready.Add(((int)x, (int)z));
        }
        Assert.Equal(14, TerrainLodSpatialNearDistance.Resolve(
            4, 14, cameraX, cameraZ, [caveTile], ExactReady));
        Assert.Equal(4, TerrainLodSpatialNearDistance.Resolve(
            14, 4, cameraX, cameraZ, [caveTile],
            static (_, _) => throw new InvalidOperationException("Shrinking needs no readiness scan.")));
    }

    [Fact]
    public void Growing_exact_radius_ignores_published_tiles_outside_the_requested_circle()
    {
        Assert.Equal(14, TerrainLodSpatialNearDistance.Resolve(
            4, 14, 0, 0, [new TerrainLodTileKey(2, 10, 10)],
            static (_, _) => throw new InvalidOperationException("Distant tile was inspected.")));
    }

    [Fact]
    public void Distant_tile_stays_authoritative_even_when_legacy_replacements_exist()
    {
        var tile = new TerrainLodTileKey(2, 10, 0);
        Assert.True(TerrainLodSpatialAuthority.ShouldPresent(tile, 0, 0, 4,
            static (_, _) => throw new InvalidOperationException("Distant selection should not scan replacements.")));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-4, -8)]
    public void Ready_columns_can_switch_independently_without_hiding_missing_neighbors(int pageX, int pageZ)
    {
        // All columns except one have a replacement. The coarse page must cover only that
        // remaining column, while its ready neighbors can immediately show their finer meshes.
        var mask = TerrainLodSpatialAuthority.HiddenColumnMask(pageX, pageZ,
            (x, z) => x == pageX + 2 && z == pageZ + 3);
        var retainedBit = 1UL << (4 * 6 + 3);
        Assert.Equal(TerrainLodSpatialAuthority.AllColumnsHidden ^ retainedBit, mask);
        Assert.NotEqual(0UL, mask & (1UL << (4 * 6 + 2)));
        Assert.Equal(0UL, mask & retainedBit);
    }

    [Fact]
    public void Page_edge_seams_keep_the_neighboring_column_in_the_coverage_mask()
    {
        var mask = TerrainLodSpatialAuthority.HiddenColumnMask(4, -4,
            static (x, z) => x == 3 && z == -4);
        Assert.Equal(TerrainLodSpatialAuthority.AllColumnsHidden ^ (1UL << 6), mask);
        Assert.Equal(TerrainLodSpatialAuthority.AllColumnsHidden,
            TerrainLodSpatialAuthority.HiddenColumnMask(4, -4, static (_, _) => false));
        Assert.Equal(0UL,
            TerrainLodSpatialAuthority.HiddenColumnMask(4, -4, static (_, _) => true));
    }

    private static void Install(
        TerrainLodSpatialPresentationSet<FakePresentation> presentations,
        TerrainLodTileKey key)
    {
        Assert.True(presentations.TryInstall(
            key, key.ToString(), () => new FakePresentation(key.ToString()), out var failure));
        Assert.Null(failure);
    }

    private static TerrainLodTileSelection Selection(TerrainLodTileKey key) =>
        new(key, 0, 16);

    private sealed class FakePresentation(string name) : IDisposable
    {
        public string Name { get; } = name;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
