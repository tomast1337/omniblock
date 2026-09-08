using OmniBlock.Client.Rendering.Chunks;

namespace OmniBlock.Tests.Rendering;

public sealed class PriorityWorkSchedulerTests
{
    [Fact]
    public async Task Urgent_work_overtakes_queued_background_work()
    {
        using PriorityWorkScheduler<string, int> scheduler = new();
        scheduler.Enqueue("background-1", 1, false);
        scheduler.Enqueue("background-2", 2, false);
        scheduler.Enqueue("urgent", 3, true);

        Assert.Equal((3, true), await scheduler.TakeAsync(CancellationToken.None));
        Assert.Equal((1, false), await scheduler.TakeAsync(CancellationToken.None));
        Assert.Equal((2, false), await scheduler.TakeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Queued_background_work_can_be_promoted_without_duplication()
    {
        using PriorityWorkScheduler<string, int> scheduler = new();
        scheduler.Enqueue("other", 1, false);
        scheduler.Enqueue("changed-chunk", 2, false);

        Assert.True(scheduler.Promote("changed-chunk"));
        Assert.Equal(2, scheduler.Count);
        Assert.Equal((2, true), await scheduler.TakeAsync(CancellationToken.None));
        Assert.Equal((1, false), await scheduler.TakeAsync(CancellationToken.None));
        Assert.Equal(0, scheduler.Count);
    }

    [Fact]
    public async Task Repeated_key_is_coalesced_and_can_be_promoted()
    {
        using PriorityWorkScheduler<string, int> scheduler = new();

        Assert.True(scheduler.Enqueue("chunk", 10, false));
        Assert.False(scheduler.Enqueue("chunk", 20, true));
        Assert.Equal(1, scheduler.Count);
        Assert.Equal((10, true), await scheduler.TakeAsync(CancellationToken.None));
    }

    [Fact]
    public void Drain_returns_each_coalesced_value_once()
    {
        using PriorityWorkScheduler<string, int> scheduler = new();
        scheduler.Enqueue("first", 1, false);
        scheduler.Enqueue("first", 2, true);
        scheduler.Enqueue("second", 3, false);

        Assert.Equal([1, 3], scheduler.Drain().Order());
        Assert.Equal(0, scheduler.Count);
    }
}
