using OmniBlock.Server.Worlds;
using OmniBlock.Util.Maths;

namespace OmniBlock.Tests.Worlds;

public sealed class FixedAreaPregenerationServiceTests
{
    [Fact]
    public void Circular_traversal_is_deterministic_and_bounded()
    {
        var positions = CircularChunkTraversal.Enumerate(10, -20, 3).ToArray();

        Assert.Equal(CircularChunkTraversal.Count(3), positions.Length);
        Assert.Equal(new ChunkPos(10, -20), positions[0]);
        Assert.Equal(positions.Length, positions.Distinct().Count());
        Assert.All(positions, position => Assert.True(
            (long)(position.X - 10) * (position.X - 10) +
            (long)(position.Z + 20) * (position.Z + 20) <= 9));
        Assert.Equal(positions, Enumerable.Range(0, positions.Length)
            .Select(index => CircularChunkTraversal.At(10, -20, 3, index)));
        var rings = positions.Select(position =>
            CircularChunkTraversal.RingDistance(10, -20, position)).ToArray();
        Assert.Equal(rings.Order(), rings);
    }

    [Fact]
    public void Circular_target_count_matches_enumeration_without_enumerating_large_areas()
    {
        for (var radius = 0; radius <= 64; radius++)
            Assert.Equal(
                CircularChunkTraversal.Enumerate(0, 0, radius).LongCount(),
                CircularChunkTraversal.Count(radius));

        Assert.Equal(52_706_921, CircularChunkTraversal.Count(4096));
    }

    [Fact]
    public void Published_progress_is_replaced_as_an_immutable_snapshot()
    {
        using var fixture = new TempDirectory();
        using var service = CreateService(fixture.Directory,
            (_, _, _) => Task.FromResult(
                new FixedAreaPregenerationWorkResult(false, false, 1, 1, 1)));

        service.Start(Definition("published", 0));
        var beforePause = Assert.Single(service.PublishedSnapshots);
        service.Pause("published");
        var afterPause = Assert.Single(service.PublishedSnapshots);

        Assert.Equal(FixedAreaPregenerationStatus.Running, beforePause.Status);
        Assert.Equal(FixedAreaPregenerationStatus.Paused, afterPause.Status);
        Assert.NotSame(beforePause, afterPause);
    }

    [Fact]
    public async Task Job_is_bounded_and_persists_completed_progress()
    {
        using var fixture = new TempDirectory();
        var active = 0;
        var peak = 0;
        var visited = new List<ChunkPos>();
        using (var service = CreateService(fixture.Directory,
                   async (_, position, token) =>
                   {
                       peak = Math.Max(peak, Interlocked.Increment(ref active));
                       try
                       {
                           await Task.Yield();
                           token.ThrowIfCancellationRequested();
                           lock (visited) visited.Add(position);
                           return new FixedAreaPregenerationWorkResult(false, false, 4, 1024, 256);
                       }
                       finally
                       {
                           Interlocked.Decrement(ref active);
                       }
                   }))
        {
            var started = service.Start(Definition("bounded", 1));
            Assert.Equal(5, started.Definition.TotalTargets);
            await TickUntil(service, "bounded", snapshot =>
                snapshot.Status == FixedAreaPregenerationStatus.Completed);
            var completed = service.Inspect("bounded");
            Assert.Equal(5, completed.SavedTargets);
            Assert.Equal(20, completed.WrittenChunks);
            Assert.Equal(1280, completed.DiskBytes);
            Assert.Equal(0, completed.RetainedBytes);
            Assert.Equal(1024, completed.PeakRetainedBytes);
            Assert.Equal(1, peak);
        }

        using var reopened = CreateService(fixture.Directory,
            (_, _, _) => throw new InvalidOperationException("Completed work restarted."));
        var restored = reopened.Inspect("bounded");
        Assert.Equal(FixedAreaPregenerationStatus.Completed, restored.Status);
        Assert.Equal(restored.Definition.TotalTargets, restored.NextTarget);
        Assert.Equal(visited, CircularChunkTraversal.Enumerate(0, 0, 1));
    }

    [SkippableFact]
    public async Task Large_area_orchestration_retains_constant_working_memory()
    {
        Skip.IfNot(
            Environment.GetEnvironmentVariable("OMNIBLOCK_RUN_LARGE_PREGEN_BENCHMARK") == "1",
            "Set OMNIBLOCK_RUN_LARGE_PREGEN_BENCHMARK=1 to run the durable large-area benchmark.");
        var radius = int.TryParse(
            Environment.GetEnvironmentVariable("OMNIBLOCK_PREGEN_BENCHMARK_RADIUS"),
            out var configuredRadius)
            ? configuredRadius
            : 64;
        Assert.InRange(radius, 16, 256);

        using var fixture = new TempDirectory();
        const int retainedPerTransaction = 4 * 1024 * 1024;
        var active = 0;
        var peakActive = 0;
        using var service = CreateService(fixture.Directory,
            async (_, _, token) =>
            {
                peakActive = Math.Max(peakActive, Interlocked.Increment(ref active));
                try
                {
                    var workspacePayload = GC.AllocateUninitializedArray<byte>(retainedPerTransaction);
                    workspacePayload[0] = 1;
                    await Task.Yield();
                    token.ThrowIfCancellationRequested();
                    GC.KeepAlive(workspacePayload);
                    return new FixedAreaPregenerationWorkResult(
                        false, false, 1, retainedPerTransaction, 1);
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            });

        GC.Collect();
        var baselineManagedBytes = GC.GetTotalMemory(forceFullCollection: true);
        var peakManagedBytes = baselineManagedBytes;
        long nextManagedSample = 64;
        var started = service.Start(Definition("large-bounded", radius));
        while (service.Inspect("large-bounded").Status != FixedAreaPregenerationStatus.Completed)
        {
            service.Tick();
            var progress = service.Inspect("large-bounded");
            if (progress.NextTarget >= nextManagedSample)
            {
                peakManagedBytes = Math.Max(
                    peakManagedBytes,
                    GC.GetTotalMemory(forceFullCollection: true));
                nextManagedSample = checked(progress.NextTarget + 64);
            }
            await Task.Yield();
        }

        var completed = service.Inspect("large-bounded");
        peakManagedBytes = Math.Max(peakManagedBytes, GC.GetTotalMemory(forceFullCollection: true));
        Assert.Equal(started.Definition.TotalTargets, completed.SavedTargets);
        Assert.Equal(1, peakActive);
        Assert.Equal(retainedPerTransaction, completed.PeakRetainedBytes);
        Assert.True(peakManagedBytes - baselineManagedBytes < 32L * 1024 * 1024,
            $"Managed working set grew by {peakManagedBytes - baselineManagedBytes:N0} bytes " +
            $"while preparing {completed.SavedTargets:N0} targets.");
    }

    [Fact]
    public void Workspace_reported_saved_targets_advance_as_skipped()
    {
        using var fixture = new TempDirectory();
        using var service = CreateService(fixture.Directory,
            (_, _, _) => Task.FromResult(
                new FixedAreaPregenerationWorkResult(true, false, 0, 0, 0)));
        service.Start(Definition("skip", 0));

        service.Tick();
        service.Tick();
        service.Tick();

        var snapshot = service.Inspect("skip");
        Assert.Equal(FixedAreaPregenerationStatus.Completed, snapshot.Status);
        Assert.Equal(1, snapshot.SkippedTargets);
        Assert.Equal(1, snapshot.PreparedTargets);
        Assert.Equal(0, snapshot.SavedTargets);
    }

    [Fact]
    public async Task Failure_diagnostic_survives_restart()
    {
        using var fixture = new TempDirectory();
        using (var service = CreateService(fixture.Directory,
                   (_, _, _) => Task.FromException<FixedAreaPregenerationWorkResult>(
                       new IOException("fixture disk failure"))))
        {
            service.Start(Definition("failed", 0));
            await TickUntil(service, "failed", snapshot =>
                snapshot.Status == FixedAreaPregenerationStatus.Failed);
        }

        using var reopened = CreateService(fixture.Directory,
            (_, _, _) => throw new InvalidOperationException());
        var failed = reopened.Inspect("failed");
        Assert.Equal(FixedAreaPregenerationStatus.Failed, failed.Status);
        Assert.Equal("storage failure", failed.ThrottleReason);
        Assert.Contains("fixture disk failure", failed.LastError);
    }

    [Fact]
    public async Task Pause_cancels_inflight_work_and_resume_retries_the_same_target()
    {
        using var fixture = new TempDirectory();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var service = CreateService(fixture.Directory,
                   async (_, _, token) =>
                   {
                       entered.TrySetResult();
                       await Task.Delay(Timeout.InfiniteTimeSpan, token);
                       throw new InvalidOperationException("Unreachable");
                   }))
        {
            service.Start(Definition("paused", 0));
            service.Tick();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(FixedAreaPregenerationStatus.Paused, service.Pause("paused").Status);
            service.Tick();
        }

        using var reopened = CreateService(fixture.Directory,
            (_, position, _) => Task.FromResult(
                new FixedAreaPregenerationWorkResult(false, false, 1, 1, 1)));
        Assert.Equal(FixedAreaPregenerationStatus.Paused, reopened.Inspect("paused").Status);
        reopened.Resume("paused");
        await TickUntil(reopened, "paused", snapshot =>
            snapshot.Status == FixedAreaPregenerationStatus.Completed);
        Assert.Equal(1, reopened.Inspect("paused").SavedTargets);
    }

    [Fact]
    public async Task World_close_turns_running_work_into_an_explicit_restart_pause()
    {
        using var fixture = new TempDirectory();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateService(fixture.Directory,
            async (_, _, token) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("Unreachable");
            });
        service.Start(Definition("world-close", 0));
        service.Tick();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        service.Dispose();

        using var reopened = CreateService(fixture.Directory,
            (_, _, _) => Task.FromResult(
                new FixedAreaPregenerationWorkResult(false, false, 1, 1, 1)));
        var snapshot = reopened.Inspect("world-close");
        Assert.Equal(FixedAreaPregenerationStatus.Paused, snapshot.Status);
        Assert.Contains("server restart", snapshot.ThrottleReason);
    }

    [Fact]
    public async Task Disk_exhaustion_has_a_distinct_throttling_reason()
    {
        using var fixture = new TempDirectory();
        using var service = CreateService(fixture.Directory,
            (_, _, _) => Task.FromException<FixedAreaPregenerationWorkResult>(
                new DiskFullException()));
        service.Start(Definition("disk-full", 0));

        await TickUntil(service, "disk-full", snapshot =>
            snapshot.Status == FixedAreaPregenerationStatus.Failed);

        Assert.Equal("disk full", service.Inspect("disk-full").ThrottleReason);
    }

    [Fact]
    public async Task A_racing_player_save_retries_without_advancing_the_target_cursor()
    {
        using var fixture = new TempDirectory();
        var attempts = 0;
        using var service = CreateService(fixture.Directory,
            (_, _, _) => Task.FromResult(Interlocked.Increment(ref attempts) == 1
                ? new FixedAreaPregenerationWorkResult(false, true, 0, 0, 0)
                : new FixedAreaPregenerationWorkResult(false, false, 1, 1, 1)));
        service.Start(Definition("race", 0));

        service.Tick();
        await TickUntil(service, "race", snapshot => attempts == 1 &&
            snapshot.ThrottleReason.Contains("retrying target"));
        Assert.Equal(0, service.Inspect("race").NextTarget);

        await TickUntil(service, "race", snapshot =>
            snapshot.Status == FixedAreaPregenerationStatus.Completed);
        Assert.Equal(2, attempts);
        Assert.Equal(1, service.Inspect("race").SavedTargets);
    }

    private static FixedAreaPregenerationService CreateService(
        DirectoryInfo directory,
        Func<FixedAreaPregenerationDefinition, ChunkPos, CancellationToken,
            Task<FixedAreaPregenerationWorkResult>> execute) =>
        new(directory, execute);

    private static FixedAreaPregenerationDefinition Definition(string id, int radius) => new(
        id, "fixture", 0, 123, "omniblock:flat", "options", "catalog",
        0, 0, radius, CircularChunkTraversal.Count(radius), DateTimeOffset.UnixEpoch);

    private static async Task TickUntil(
        FixedAreaPregenerationService service,
        string id,
        Func<FixedAreaPregenerationSnapshot, bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!predicate(service.Inspect(id)))
        {
            service.Tick();
            if (DateTime.UtcNow >= deadline) throw new TimeoutException();
            await Task.Delay(1);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Directory = new DirectoryInfo(Path.Combine(
                Path.GetTempPath(), $"omniblock-pregen-{Guid.NewGuid():N}"));
            Directory.Create();
        }

        public DirectoryInfo Directory { get; }

        public void Dispose()
        {
            if (Directory.Exists) Directory.Delete(recursive: true);
        }
    }

    private sealed class DiskFullException : IOException
    {
        public DiskFullException() : base("No space left on device")
        {
            HResult = unchecked((int)0x80070070);
        }
    }
}
