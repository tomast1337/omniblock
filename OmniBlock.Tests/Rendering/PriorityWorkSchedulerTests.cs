using OmniBlock.Client.Rendering.Chunks;

namespace OmniBlock.Tests.Rendering;

public sealed class PriorityWorkSchedulerTests
{
    [Fact]
    public async Task Critical_work_overtakes_foreground_and_background_work()
    {
        using PriorityWorkScheduler<string, int> scheduler = new();
        scheduler.Enqueue("background", 1, MeshWorkPriority.Background);
        scheduler.Enqueue("foreground", 2, MeshWorkPriority.Foreground);
        scheduler.Enqueue("critical", 3, MeshWorkPriority.Critical);

        Assert.Equal((3, MeshWorkPriority.Critical), await scheduler.TakeAsync(CancellationToken.None));
        Assert.Equal((2, MeshWorkPriority.Foreground), await scheduler.TakeAsync(CancellationToken.None));
        Assert.Equal((1, MeshWorkPriority.Background), await scheduler.TakeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Queued_background_work_can_be_promoted_without_duplication()
    {
        using PriorityWorkScheduler<string, int> scheduler = new();
        scheduler.Enqueue("other", 1, MeshWorkPriority.Background);
        scheduler.Enqueue("changed-chunk", 2, MeshWorkPriority.Background);

        Assert.True(scheduler.Promote("changed-chunk", MeshWorkPriority.Critical));
        Assert.Equal(2, scheduler.Count);
        Assert.Equal((2, MeshWorkPriority.Critical), await scheduler.TakeAsync(CancellationToken.None));
        Assert.Equal((1, MeshWorkPriority.Background), await scheduler.TakeAsync(CancellationToken.None));
        Assert.Equal(0, scheduler.Count);
    }

    [Fact]
    public async Task Repeated_key_is_coalesced_and_can_be_promoted()
    {
        using PriorityWorkScheduler<string, int> scheduler = new();

        Assert.True(scheduler.Enqueue("chunk", 10, MeshWorkPriority.Background));
        Assert.False(scheduler.Enqueue("chunk", 20, MeshWorkPriority.Foreground));
        Assert.False(scheduler.Enqueue("chunk", 30, MeshWorkPriority.Critical));
        Assert.Equal(1, scheduler.Count);
        Assert.Equal((10, MeshWorkPriority.Critical), await scheduler.TakeAsync(CancellationToken.None));
    }

    [Fact]
    public void Drain_returns_each_coalesced_value_once()
    {
        using PriorityWorkScheduler<string, int> scheduler = new();
        scheduler.Enqueue("first", 1, MeshWorkPriority.Background);
        scheduler.Enqueue("first", 2, MeshWorkPriority.Critical);
        scheduler.Enqueue("second", 3, MeshWorkPriority.Foreground);

        Assert.Equal([1, 3], scheduler.Drain().Order());
        Assert.Equal(0, scheduler.Count);
    }

    [Fact]
    public async Task Sustained_critical_work_cannot_starve_loading_lanes()
    {
        using PriorityWorkScheduler<string, int> scheduler = new();
        scheduler.Enqueue("foreground", 100, MeshWorkPriority.Foreground);
        scheduler.Enqueue("background", 200, MeshWorkPriority.Background);
        for (var i = 0; i < 32; i++)
            scheduler.Enqueue($"critical-{i}", i, MeshWorkPriority.Critical);

        List<(int Value, MeshWorkPriority Priority)> drained = [];
        for (var i = 0; i < 18; i++)
            drained.Add(await scheduler.TakeAsync(CancellationToken.None));

        Assert.Equal(MeshWorkPriority.Critical, drained[0].Priority);
        Assert.InRange(
            drained.FindIndex(static entry => entry.Priority == MeshWorkPriority.Foreground),
            1,
            MeshPriorityFairness.CriticalBurstLimit);
        Assert.InRange(
            drained.FindIndex(static entry => entry.Priority == MeshWorkPriority.Background),
            1,
            MeshPriorityFairness.HigherPriorityBurstLimit + 1);
    }
}
