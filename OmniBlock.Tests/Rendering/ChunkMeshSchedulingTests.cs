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
            ChunkRenderer.ShouldPrioritizeMesh(false, false, false), false, 10, 20);
        for (var x = 4; x < 32; x++)
        {
            // The world reports full chunk arrivals using an urgent dirty notification too.
            var streamed = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(x * 16, 64, 0), View,
                ChunkRenderer.ShouldPrioritizeMesh(true, false, false), true, 0, 20);
            Assert.True(nearby.CompareTo(streamed) < 0);
        }

        var edit = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(64, 64, 0), View,
            ChunkRenderer.ShouldPrioritizeMesh(true, true, false), true, 20, 20);
        Assert.True(edit.CompareTo(nearby) < 0);

        var startup = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(16, 64, 0), View,
            ChunkRenderer.ShouldPrioritizeMesh(true, false, true), false, 20, 20);
        Assert.True(startup.CompareTo(nearby) < 0);
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
    public void Older_work_wins_within_the_same_tier()
    {
        var older = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(16 * 2, 64, 0), View,
            false, false, 5, 20);
        var newer = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(16, 64, 0), View,
            false, false, 10, 20);

        Assert.True(older.CompareTo(newer) < 0);
    }

    [Fact]
    public void Prediction_favors_meshes_ahead_of_player_motion()
    {
        var predicted = ChunkRenderer.PredictMeshCenter(View, new Vector3D<double>(1, 0, 0));
        var ahead = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(16 * 8, 64, 0), View, predicted,
            false, true, 10, 10);
        var behind = ChunkRenderer.GetMeshSchedulingRank(new Vector3D<int>(-16 * 8, 64, 0), View, predicted,
            false, true, 10, 10);

        Assert.True(ahead.CompareTo(behind) < 0);
    }

    [Fact]
    public void Prediction_uses_half_a_second_of_game_velocity()
    {
        Assert.Equal(
            new Vector3D<double>(18, 72, -2),
            ChunkRenderer.PredictMeshCenter(View, new Vector3D<double>(1, 0, -1)));
    }
}
