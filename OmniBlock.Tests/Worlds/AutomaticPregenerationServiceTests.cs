using OmniBlock.Server.Worlds;
using OmniBlock.Util.Maths;

namespace OmniBlock.Tests.Worlds;

public sealed class AutomaticPregenerationServiceTests
{
    [Fact]
    public void Pressure_gate_uses_lower_resume_thresholds()
    {
        var overloaded = Pressure(serverTickMs: 41);
        Assert.Contains("server tick", AutomaticPregenerationService.EvaluatePressure(
            AutomaticPregenerationProfile.Play, overloaded, alreadyThrottled: false));
        Assert.Contains("server tick", AutomaticPregenerationService.EvaluatePressure(
            AutomaticPregenerationProfile.Play, Pressure(serverTickMs: 31), alreadyThrottled: true));
        Assert.Null(AutomaticPregenerationService.EvaluatePressure(
            AutomaticPregenerationProfile.Play, Pressure(serverTickMs: 29), alreadyThrottled: true));

        Assert.Contains("player chunk", AutomaticPregenerationService.EvaluatePressure(
            AutomaticPregenerationProfile.Play, Pressure(gameplayPending: 1), false));
        Assert.Contains("client frame", AutomaticPregenerationService.EvaluatePressure(
            AutomaticPregenerationProfile.Play, Pressure(clientFrameMs: 26), false));
        Assert.Contains("lighting", AutomaticPregenerationService.EvaluatePressure(
            AutomaticPregenerationProfile.Play, Pressure(lightingPending: 4_097), false));
        Assert.Contains("memory", AutomaticPregenerationService.EvaluatePressure(
            AutomaticPregenerationProfile.Play, Pressure(memoryLoadRatio: .86), false));
        Assert.Contains("disk", AutomaticPregenerationService.EvaluatePressure(
            AutomaticPregenerationProfile.Play, Pressure(diskFreeBytes: 1024), false));
    }

    [Fact]
    public async Task Identical_player_areas_share_one_radial_target_set()
    {
        List<ChunkPos> visited = [];
        using var service = Service(
            new AutomaticPregenerationOptions(true, 4, AutomaticPregenerationProfile.Preparation),
            (position, _, _) =>
            {
                visited.Add(position);
                return Task.FromResult(new FixedAreaPregenerationWorkResult(
                    false, false, 1, 1024, 1));
            });
        AutomaticGenerationPlayer[] players =
        [
            new(1, 10, -20, 1, 0),
            new(2, 10, -20, 0, 0)
        ];

        await TickUntil(service, players,
            snapshot => snapshot.ThrottleReason == "current moving area is prepared");

        Assert.Equal(CircularChunkTraversal.Count(4), visited.Count);
        Assert.Equal(visited.Count, visited.Distinct().Count());
        Assert.Equal(new ChunkPos(10, -20), visited[0]);
        Assert.Equal(visited.Count, service.Snapshot().SavedTargets);
    }

    [Fact]
    public async Task Teleport_cancels_an_obsolete_target_and_restarts_at_the_new_center()
    {
        var firstEntered = new TaskCompletionSource<ChunkPos>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        List<ChunkPos> completed = [];
        using var service = Service(
            new AutomaticPregenerationOptions(true, 8, AutomaticPregenerationProfile.Preparation),
            async (position, _, token) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    firstEntered.TrySetResult(position);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                completed.Add(position);
                return new FixedAreaPregenerationWorkResult(false, false, 1, 1, 1);
            });

        service.Tick([new AutomaticGenerationPlayer(1, 0, 0, 0, 0)]);
        Assert.Equal(new ChunkPos(0, 0),
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        service.Tick([new AutomaticGenerationPlayer(1, 100, -100, 0, 0)]);

        await TickUntil(service,
            [new AutomaticGenerationPlayer(1, 100, -100, 0, 0)],
            snapshot => completed.Count > 0);
        Assert.Equal(new ChunkPos(100, -100), completed[0]);
        Assert.DoesNotContain(new ChunkPos(0, 0), completed);
    }

    [Fact]
    public void Play_and_preparation_profiles_have_explicit_caps_and_pause_behavior()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Service(
            new AutomaticPregenerationOptions(true, 65, AutomaticPregenerationProfile.Play),
            Success));
        using var play = Service(
            new AutomaticPregenerationOptions(true, 64, AutomaticPregenerationProfile.Play),
            Success);
        using var preparation = Service(
            new AutomaticPregenerationOptions(true, 256, AutomaticPregenerationProfile.Preparation),
            Success);

        Assert.False(play.RunsWhileSimulationPaused);
        Assert.True(preparation.RunsWhileSimulationPaused);
    }

    [Fact]
    public void Pressure_prevents_admission_until_it_clears()
    {
        var pressure = Pressure(gameplayPending: 1);
        var calls = 0;
        using var service = new AutomaticPregenerationService(
            0,
            new AutomaticPregenerationOptions(true, 4, AutomaticPregenerationProfile.Play),
            (_, _, _) =>
            {
                calls++;
                return Success(default, 0, default);
            },
            () => pressure);
        var players = new[] { new AutomaticGenerationPlayer(1, 0, 0, 0, 0) };

        service.Tick(players);
        Assert.Equal(0, calls);
        Assert.True(service.Snapshot().Throttled);

        pressure = Pressure();
        service.Tick(players);
        Assert.Equal(1, calls);
        Assert.False(service.Snapshot().Throttled);
    }

    [Fact]
    public async Task Gameplay_owned_targets_are_deferred_without_starting_background_work()
    {
        List<ChunkPos> visited = [];
        var owned = new ChunkPos(0, 0);
        using var service = new AutomaticPregenerationService(
            0,
            new AutomaticPregenerationOptions(true, 4,
                AutomaticPregenerationProfile.Preparation),
            (position, _, _) =>
            {
                visited.Add(position);
                return Success(default, 0, default);
            },
            () => Pressure(),
            position => position == owned);
        AutomaticGenerationPlayer[] players = [new(1, 0, 0, 0, 0)];

        await TickUntil(service, players, snapshot => snapshot.SavedTargets >= 1);

        Assert.DoesNotContain(owned, visited);
        Assert.Equal(1, service.Snapshot().GameplayDeferredTargets);
        Assert.Equal(1, CircularChunkTraversal.RingDistance(0, 0, visited[0]));
    }

    private static AutomaticPregenerationService Service(
        AutomaticPregenerationOptions options,
        Func<ChunkPos, int, CancellationToken, Task<FixedAreaPregenerationWorkResult>> execute) =>
        new(0, options, execute, () => Pressure());

    private static Task<FixedAreaPregenerationWorkResult> Success(
        ChunkPos _, int __, CancellationToken ___) =>
        Task.FromResult(new FixedAreaPregenerationWorkResult(false, false, 1, 1, 1));

    private static AutomaticPregenerationPressure Pressure(
        int gameplayPending = 0,
        int backgroundQueued = 0,
        int lightingPending = 0,
        double serverTickMs = 0,
        double? clientFrameMs = null,
        double memoryLoadRatio = 0,
        long diskFreeBytes = long.MaxValue) =>
        new(gameplayPending, backgroundQueued, lightingPending, serverTickMs,
            clientFrameMs, memoryLoadRatio, diskFreeBytes);

    private static async Task TickUntil(
        AutomaticPregenerationService service,
        IReadOnlyList<AutomaticGenerationPlayer> players,
        Func<AutomaticPregenerationSnapshot, bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!predicate(service.Snapshot()))
        {
            service.Tick(players);
            if (DateTime.UtcNow >= deadline) throw new TimeoutException();
            await Task.Yield();
        }
    }
}
