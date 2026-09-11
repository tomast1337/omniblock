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

        Assert.True(queue.Enqueue(Request(pos, 1, MeshWorkPriority.Background), (3, 100, 1)));
        Assert.False(queue.Enqueue(Request(pos, 1, MeshWorkPriority.Background), (3, 100, 1)));
        Assert.True(queue.Promote(pos, MeshWorkPriority.Foreground, (1, 100, 1)));

        Assert.Equal(1, queue.Count);
        Assert.Equal(1, queue.CountPriority(MeshWorkPriority.Foreground));
        Assert.True(queue.TryDequeue(out var result));
        Assert.Equal(pos, result.Pos);
        Assert.Equal(MeshWorkPriority.Foreground, result.Priority);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Critical_and_foreground_lanes_overtake_closer_background_work()
    {
        SectionMeshRequestQueue queue = new();
        queue.Enqueue(Request(new Vector3D<int>(0, 64, 0), 1, MeshWorkPriority.Background), (3, 0, 1));
        queue.Enqueue(Request(new Vector3D<int>(160, 64, 0), 2, MeshWorkPriority.Foreground), (1, 100, 2));
        queue.Enqueue(Request(new Vector3D<int>(320, 64, 0), 3, MeshWorkPriority.Critical), (0, 400, 3));

        Assert.True(queue.TryDequeue(out var critical));
        Assert.True(queue.TryDequeue(out var foreground));
        Assert.True(queue.TryDequeue(out var background));

        Assert.Equal(MeshWorkPriority.Critical, critical.Priority);
        Assert.Equal(MeshWorkPriority.Foreground, foreground.Priority);
        Assert.Equal(MeshWorkPriority.Background, background.Priority);
    }

    [Fact]
    public void Reprioritizing_after_camera_movement_changes_order_within_a_lane()
    {
        SectionMeshRequestQueue queue = new();
        var formerlyNear = Request(new Vector3D<int>(0, 64, 0), 1, MeshWorkPriority.Background);
        var newlyNear = Request(new Vector3D<int>(160, 64, 0), 2, MeshWorkPriority.Background);
        queue.Enqueue(formerlyNear, (3, 0, 1));
        queue.Enqueue(newlyNear, (3, 100, 2));

        queue.Reprioritize(info => (3, info.Pos == newlyNear.Pos ? 0 : 100, info.EnqueuedAt));

        Assert.True(queue.TryDequeue(out var result));
        Assert.Equal(newlyNear.Pos, result.Pos);
    }

    [Fact]
    public void Removing_evicted_sections_leaves_no_live_heap_entry()
    {
        SectionMeshRequestQueue queue = new();
        queue.Enqueue(Request(new Vector3D<int>(0, 64, 0), 1, MeshWorkPriority.Background), (3, 0, 1));
        queue.Enqueue(Request(new Vector3D<int>(160, 64, 0), 2, MeshWorkPriority.Background), (3, 100, 2));

        queue.RemoveWhere(info => info.Pos.X == 0);

        Assert.Equal(1, queue.Count);
        Assert.True(queue.TryDequeue(out var result));
        Assert.Equal(160, result.Pos.X);
        Assert.False(queue.TryDequeue(out _));
    }

    private static ChunkToMeshInfo Request(
        Vector3D<int> position,
        long version,
        MeshWorkPriority priority) => new(position, version, priority, version);
}
