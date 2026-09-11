using OmniBlock.Client.Rendering.Chunks;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class ChunkMeshSchedulingTests
{
    private static readonly Vector3D<double> View = new(8, 72, 8);

    [Fact]
    public void Streamed_chunk_notifications_cannot_overtake_a_missing_safety_ring_mesh()
    {
        var nearby = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(32, 64, 0), View,
            MeshWorkPriority.Background, false, 10, 20);
        for (var x = 4; x < 32; x++)
        {
            // The world reports full chunk arrivals using an urgent dirty notification too.
            var streamed = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(x * 16, 64, 0), View,
                ChunkRenderer.ClassifyRequestedMeshPriority(true, false, false), true, 0, 20);
            Assert.True(nearby.CompareTo(streamed) < 0);
        }

        var edit = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(64, 64, 0), View,
            ChunkRenderer.ClassifyRequestedMeshPriority(true, true, false), true, 20, 20);
        Assert.True(edit.CompareTo(nearby) < 0);

        var startup = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(16 * 8, 64, 0), View,
            ChunkRenderer.ClassifyRequestedMeshPriority(true, false, true), false, 20, 20);
        var ordinaryVisible = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(16 * 7, 64, 0), View,
            MeshWorkPriority.Background, true, 10, 20);
        Assert.True(startup.CompareTo(ordinaryVisible) < 0);
    }

    [Theory]
    [InlineData(true, true, false, (int)MeshWorkPriority.Critical)]
    [InlineData(true, false, true, (int)MeshWorkPriority.Foreground)]
    [InlineData(false, false, true, (int)MeshWorkPriority.Foreground)]
    [InlineData(true, false, false, (int)MeshWorkPriority.Background)]
    public void Dirty_notifications_are_classified_without_promoting_streaming_to_gameplay_priority(
        bool updateRequested,
        bool hasRenderer,
        bool requiredForStartup,
        int expected)
    {
        Assert.Equal(
            (MeshWorkPriority)expected,
            ChunkRenderer.ClassifyRequestedMeshPriority(updateRequested, hasRenderer, requiredForStartup));
    }

    [Theory]
    [InlineData(3, 0, true)]
    [InlineData(-2, 2, true)]
    [InlineData(-3, 3, false)]
    [InlineData(4, 0, false)]
    [InlineData(0, -4, false)]
    public void Safety_ring_is_radial_in_horizontal_chunk_space(int chunkX, int chunkZ, bool expected)
    {
        var position = new Vector3D<int>(chunkX * 16, 0, chunkZ * 16);

        Assert.Equal(expected, ChunkRenderer.IsInMeshSafetyRing(position, View));
    }

    [Fact]
    public void Equal_distance_discovery_pairs_opposite_directions()
    {
        List<Vector2D<int>> offsets =
        [
            new(-2, -2),
            new(-2, 2),
            new(2, -2),
            new(2, 2),
            new(-3, 0),
            new(0, -3),
            new(0, 3),
            new(3, 0)
        ];

        offsets.Sort(ChunkRenderer.CompareBalancedHorizontalOffsets);

        for (var i = 0; i < offsets.Count; i += 2)
            Assert.Equal(-offsets[i], offsets[i + 1]);
    }

    [Fact]
    public void Urgent_work_outranks_the_safety_ring()
    {
        var urgent = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(16 * 10, 64, 0), View,
            true, false, 10, 10);
        var safety = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(16 * 2, 64, 0), View,
            false, false, 0, 10);

        Assert.True(urgent.CompareTo(safety) < 0);
    }

    [Fact]
    public void Safety_ring_outranks_new_visible_work()
    {
        var safety = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(16 * 3, 64, 0), View,
            false, false, 10, 10);
        var visible = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(16 * 8, 64, 0), View,
            false, true, 10, 10);

        Assert.True(safety.CompareTo(visible) < 0);
    }

    [Fact]
    public void Aged_background_work_promotes_to_visible_but_not_the_safety_ring()
    {
        var position = new Vector3D<int>(16 * 10, 64, 0);
        var fresh = ChunkRenderer.GetMeshSchedulingRank(position, View, false, false, 0, 0);
        var onePromotion = ChunkRenderer.GetMeshSchedulingRank(position, View, false, false, 0,
            ChunkRenderer.MeshAgePromotionTicks);
        var fullyPromoted = ChunkRenderer.GetMeshSchedulingRank(position, View, false, false, 0,
            ChunkRenderer.MeshAgePromotionTicks * 20);

        Assert.Equal(3, fresh.Tier);
        Assert.Equal(2, onePromotion.Tier);
        Assert.Equal(2, fullyPromoted.Tier);
    }

    [Fact]
    public void Closer_work_wins_within_the_same_tier_even_when_it_is_newer()
    {
        var olderFar = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(16 * 3, 64, 0), View,
            false, false, 5, 20);
        var newerClose = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(16, 64, 0), View,
            false, false, 10, 20);

        Assert.True(newerClose.CompareTo(olderFar) < 0);
    }

    [Theory]
    [InlineData(false, false, false, false, (int)MeshWorkPriority.Background)]
    [InlineData(false, false, false, true, (int)MeshWorkPriority.Foreground)]
    public void Missing_safety_ring_meshes_use_the_foreground_lane(
        bool updateRequested,
        bool hasRenderer,
        bool requiredForStartup,
        bool withinSafetyRing,
        int expected)
    {
        Assert.Equal(
            (MeshWorkPriority)expected,
            ChunkRenderer.ClassifyRequestedMeshPriority(
                updateRequested, hasRenderer, requiredForStartup, withinSafetyRing));
    }

    [Theory]
    [InlineData((int)SectionDirtyReason.InitialTerrain, false, false, false, true,
        (int)MeshWorkPriority.Foreground)]
    [InlineData((int)SectionDirtyReason.InitialTerrain, false, false, false, false,
        (int)MeshWorkPriority.Background)]
    [InlineData((int)SectionDirtyReason.StreamingBoundary, true, false, true, true,
        (int)MeshWorkPriority.Background)]
    [InlineData((int)SectionDirtyReason.BlockChange, true, false, true, true,
        (int)MeshWorkPriority.Critical)]
    [InlineData((int)SectionDirtyReason.Lighting, true, false, true, true,
        (int)MeshWorkPriority.Critical)]
    [InlineData((int)SectionDirtyReason.Lighting, true, false, false, true,
        (int)MeshWorkPriority.Background)]
    public void Dirty_reason_controls_lane_without_displacing_missing_foreground_terrain(
        int reason,
        bool hasRenderer,
        bool requiredForStartup,
        bool withinSafetyRing,
        bool withinForegroundRing,
        int expected)
    {
        Assert.Equal(
            (MeshWorkPriority)expected,
            ChunkRenderer.ClassifyRequestedMeshPriority(
                (SectionDirtyReason)reason,
                hasRenderer,
                requiredForStartup,
                withinSafetyRing,
                withinForegroundRing));
    }

    [Theory]
    [InlineData(8, 0, true)]
    [InlineData(-6, 5, true)]
    [InlineData(8, 1, false)]
    [InlineData(9, 0, false)]
    public void Foreground_preparation_ring_is_radial(int chunkX, int chunkZ, bool expected)
    {
        var position = new Vector3D<int>(chunkX * 16, 64, chunkZ * 16);

        Assert.Equal(expected, ChunkRenderer.IsInMeshForegroundRing(position, View));
    }

    [Theory]
    [InlineData((int)SectionDirtyReason.BlockChange, false, false, false, true)]
    [InlineData((int)SectionDirtyReason.StreamingBoundary, false, false, false, true)]
    [InlineData((int)SectionDirtyReason.BlockChange, true, false, false, false)]
    [InlineData((int)SectionDirtyReason.BlockChange, false, true, false, false)]
    [InlineData((int)SectionDirtyReason.BlockChange, false, false, true, false)]
    [InlineData((int)SectionDirtyReason.InitialTerrain, false, false, false, false)]
    public void Unmeshed_updates_wait_for_bounded_discovery_unless_a_build_must_be_superseded(
        int reason,
        bool hasRenderer,
        bool requiredForStartup,
        bool hasPendingBuild,
        bool expected)
    {
        Assert.Equal(expected, ChunkRenderer.ShouldWaitForMissingMeshDiscovery(
            (SectionDirtyReason)reason,
            hasRenderer,
            requiredForStartup,
            hasPendingBuild));
    }

    [Fact]
    public void Prediction_favors_meshes_ahead_of_player_motion()
    {
        var predicted = ChunkRenderer.PredictMeshCenter(View, new Vector3D<double>(1, 0, 0));
        var ahead = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(16 * 8, 64, 0), View, predicted,
            false, true, false, 10, 10);
        var behind = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(-16 * 8, 64, 0), View, predicted,
            false, true, false, 10, 10);

        Assert.True(ahead.CompareTo(behind) < 0);
    }

    [Fact]
    public void Prediction_uses_half_a_second_of_game_velocity()
    {
        Assert.Equal(
            new Vector3D<double>(18, 72, -2),
            ChunkRenderer.PredictMeshCenter(View, new Vector3D<double>(1, 0, -1)));
    }

    [Theory]
    [InlineData(64, 4)]
    [InlineData(164, 7)]
    [InlineData(-40, 0)]
    public void Discovery_center_is_clamped_to_world_sections(double viewY, int expectedSectionY)
    {
        var center = ChunkRenderer.GetMeshDiscoveryCenter(new Vector3D<double>(0, viewY, 0));

        Assert.Equal(expectedSectionY, center.Y);
    }

    [Fact]
    public void Speculative_prefetch_is_a_bounded_cap_ahead_of_motion()
    {
        const int renderDistance = 4;
        var predicted = ChunkRenderer.PredictMeshCenter(View, new Vector3D<double>(1, 0, 0));

        Assert.True(ChunkRenderer.IsSpeculativePrefetchChunk(
            new Vector3D<int>(5 * 16, 64, 0), View, predicted, renderDistance));
        Assert.False(ChunkRenderer.IsSpeculativePrefetchChunk(
            new Vector3D<int>(-5 * 16, 64, 0), View, predicted, renderDistance));
        Assert.False(ChunkRenderer.IsSpeculativePrefetchChunk(
            new Vector3D<int>(6 * 16, 64, 0), View, predicted, renderDistance));
        Assert.False(ChunkRenderer.IsSpeculativePrefetchChunk(
            new Vector3D<int>(5 * 16, 64, 0), View, View, renderDistance));
    }

    [Fact]
    public void Speculative_prefetch_stays_below_drawable_background_work()
    {
        var background = ChunkRenderer.GetMeshSchedulingRank(
            new Vector3D<int>(4 * 16, 64, 0), View, View, false, false, false, 10, 10);
        var speculative = ChunkRenderer.GetMeshSchedulingRank(
            new Vector3D<int>(5 * 16, 64, 0), View, new Vector3D<double>(18, 72, 8),
            false, true, true, 10, 10);

        Assert.Equal(3, background.Tier);
        Assert.Equal(4, speculative.Tier);
        Assert.True(background.CompareTo(speculative) < 0);
    }

    [Fact]
    public void Leading_edge_contains_only_columns_newly_entering_the_predicted_prepare_disk()
    {
        Vector3D<int> previous = new(0, 4, 0);
        Vector3D<int> current = new(1, 4, 0);
        var edge = ChunkRenderer.GetLeadingEdgeColumns(previous, current, 4);

        Assert.NotEmpty(edge);
        Assert.Contains(new Vector2D<int>(6, 0), edge); // one cap column ahead of current R
        Assert.DoesNotContain(new Vector2D<int>(-4, 0), edge); // retained trailing boundary
        Assert.All(edge, column =>
        {
            var oldDx = column.X - previous.X;
            var oldDz = column.Y - previous.Z;
            Assert.True(oldDx * oldDx + oldDz * oldDz > 16);
            var futureDx = column.X - 2;
            var futureDz = column.Y;
            Assert.True(futureDx * futureDx + futureDz * futureDz <= 16);
            var currentDx = column.X - current.X;
            var currentDz = column.Y - current.Z;
            Assert.True(currentDx * currentDx + currentDz * currentDz <= 25);
        });
    }

    [Fact]
    public void Vertical_section_crossings_do_not_create_a_horizontal_leading_edge()
    {
        Assert.Empty(ChunkRenderer.GetLeadingEdgeColumns(
            new Vector3D<int>(3, 4, -2),
            new Vector3D<int>(3, 5, -2),
            4));
    }

    [Fact]
    public void Leading_edge_rotates_with_motion_and_orders_the_front_first()
    {
        Vector3D<int> previous = new(10, 4, 10);
        Vector3D<int> current = new(9, 4, 11);
        var edge = ChunkRenderer.GetLeadingEdgeColumns(previous, current, 4);

        Assert.Contains(new Vector2D<int>(8, 15), edge);
        var projections = edge.Select(column => -column.X + column.Y).ToArray();
        Assert.Equal(projections.OrderDescending(), projections);
    }

    [Theory]
    [InlineData(4, 0, 4, true)]
    [InlineData(6, 0, 4, true)]
    [InlineData(7, 0, 4, false)]
    [InlineData(-6, 0, 4, true)]
    public void Retention_boundary_has_a_two_chunk_margin(
        int chunkX, int chunkZ, int prepareRadius, bool expected)
    {
        var position = new Vector3D<int>(chunkX * 16, 64, chunkZ * 16);
        Assert.Equal(expected, ChunkRenderer.IsInHorizontalChunkRadius(
            position, View, prepareRadius + ChunkRenderer.MeshRetentionMargin));
    }
}
