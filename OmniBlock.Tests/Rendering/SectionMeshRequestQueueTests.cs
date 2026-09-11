using OmniBlock.Client.Rendering.Chunks;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class SectionMeshRequestQueueTests
{
    [Fact]
    public void Repeated_section_is_coalesced_and_promoted_without_a_scan()
    {
        SectionMeshRequestQueue queue = new();
        var pos = new Vector3D<int>(16, 64, 16);
        using var state = State(pos, MeshWorkPriority.Background, 1);

        Assert.True(queue.Enqueue(state, (3, int.MaxValue, 100, 1)));
        Assert.False(queue.Enqueue(state, (3, int.MaxValue, 100, 1)));
        state.RememberRequest(SectionDirtyReason.InitialTerrain, MeshWorkPriority.Foreground, 2);
        Assert.True(queue.Promote(pos, (1, int.MaxValue, 100, 1)));

        Assert.Equal(1, queue.Count);
        Assert.Equal(1, queue.CountPriority(MeshWorkPriority.Foreground));
        Assert.True(queue.TryDequeue(out var result));
        Assert.Same(state, result);
        Assert.Equal(MeshWorkPriority.Foreground, result.RequestedPriority);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Critical_and_foreground_lanes_overtake_closer_background_work()
    {
        SectionMeshRequestQueue queue = new();
        using var backgroundState = State(new Vector3D<int>(0, 64, 0), MeshWorkPriority.Background, 1);
        using var foregroundState = State(new Vector3D<int>(160, 64, 0), MeshWorkPriority.Foreground, 2);
        using var criticalState = State(new Vector3D<int>(320, 64, 0), MeshWorkPriority.Critical, 3);
        queue.Enqueue(backgroundState, (3, int.MaxValue, 0, 1));
        queue.Enqueue(foregroundState, (1, int.MaxValue, 100, 2));
        queue.Enqueue(criticalState, (0, 10, 400, 3));

        Assert.True(queue.TryDequeue(out var critical));
        Assert.True(queue.TryDequeue(out var foreground));
        Assert.True(queue.TryDequeue(out var background));

        Assert.Equal(MeshWorkPriority.Critical, critical.RequestedPriority);
        Assert.Equal(MeshWorkPriority.Foreground, foreground.RequestedPriority);
        Assert.Equal(MeshWorkPriority.Background, background.RequestedPriority);
    }

    [Fact]
    public void Reprioritizing_after_camera_movement_changes_order_within_a_lane()
    {
        SectionMeshRequestQueue queue = new();
        using var formerlyNear = State(new Vector3D<int>(0, 64, 0), MeshWorkPriority.Background, 1);
        using var newlyNear = State(new Vector3D<int>(160, 64, 0), MeshWorkPriority.Background, 2);
        queue.Enqueue(formerlyNear, (3, int.MaxValue, 0, 1));
        queue.Enqueue(newlyNear, (3, int.MaxValue, 100, 2));

        queue.Reprioritize(state =>
            (3, int.MaxValue, state.Position == newlyNear.Position ? 0 : 100, state.RequestedAt));

        Assert.True(queue.TryDequeue(out var result));
        Assert.Same(newlyNear, result);
    }

    [Fact]
    public void Removing_evicted_sections_leaves_no_live_heap_entry()
    {
        SectionMeshRequestQueue queue = new();
        using var removedState = State(new Vector3D<int>(0, 64, 0), MeshWorkPriority.Background, 1);
        using var retainedState = State(new Vector3D<int>(160, 64, 0), MeshWorkPriority.Background, 2);
        queue.Enqueue(removedState, (3, int.MaxValue, 0, 1));
        queue.Enqueue(retainedState, (3, int.MaxValue, 100, 2));

        queue.RemoveWhere(state => state.Position.X == 0);

        Assert.Equal(1, queue.Count);
        Assert.True(queue.TryDequeue(out var result));
        Assert.Same(retainedState, result);
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public void Earlier_critical_deadline_beats_distance_within_the_critical_lane()
    {
        SectionMeshRequestQueue queue = new();
        using var nearLater = State(new Vector3D<int>(0, 64, 0), MeshWorkPriority.Critical, 1);
        using var farEarlier = State(new Vector3D<int>(320, 64, 0), MeshWorkPriority.Critical, 2);
        queue.Enqueue(nearLater, (0, 20, 0, 1));
        queue.Enqueue(farEarlier, (0, 10, 400, 2));

        Assert.True(queue.TryDequeue(out var result));
        Assert.Same(farEarlier, result);
    }

    private static SectionRenderState State(
        Vector3D<int> position,
        MeshWorkPriority priority,
        long requestedAt)
    {
        var state = new SectionRenderState(position);
        state.RememberRequest(SectionDirtyReason.InitialTerrain, priority, requestedAt);
        state.Version.MarkDirty();
        state.Version.SnapshotIfNeeded();
        return state;
    }
}
