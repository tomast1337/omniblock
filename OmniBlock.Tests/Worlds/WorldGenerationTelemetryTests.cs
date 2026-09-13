using OmniBlock.Server.Worlds;

namespace OmniBlock.Tests.Worlds;

public sealed class WorldGenerationTelemetryTests
{
    [Fact]
    public void Snapshot_reports_bounded_stage_distributions_failures_and_pressure_gauges()
    {
        var telemetry = new WorldGenerationTelemetry();
        for (var i = 1; i <= WorldGenerationTelemetry.SampleCapacity + 20; i++)
        {
            var value = telemetry.Measure(WorldGenerationStage.Terrain, () => new byte[i]);
            Assert.Equal(i, value.Length);
        }

        Assert.Throws<InvalidOperationException>(() =>
            telemetry.Measure<int>(WorldGenerationStage.Decoration,
                () => throw new InvalidOperationException("fixture")));
        telemetry.SetQueueDepths(5, 2, 3);
        telemetry.SetQueueDepths(1, 1, 0);
        telemetry.SetResidency(12, 983040);

        var snapshot = telemetry.Snapshot();
        var terrain = snapshot.Stages[WorldGenerationStage.Terrain];
        var decoration = snapshot.Stages[WorldGenerationStage.Decoration];

        Assert.Equal(WorldGenerationTelemetry.SampleCapacity + 20, terrain.Count);
        Assert.True(terrain.P50Ms >= 0);
        Assert.True(terrain.P95Ms >= terrain.P50Ms);
        Assert.True(terrain.P99Ms >= terrain.P95Ms);
        Assert.True(terrain.MaxMs >= terrain.P99Ms);
        Assert.True(terrain.MaxAllocatedBytes >= terrain.P95AllocatedBytes);
        Assert.Equal(1, decoration.Count);
        Assert.Equal(1, decoration.Failures);
        Assert.Equal(1, snapshot.Failures);
        Assert.Equal(1, snapshot.Queued);
        Assert.Equal(1, snapshot.InFlight);
        Assert.Equal(0, snapshot.Ready);
        Assert.Equal(10, snapshot.QueuePeak);
        Assert.Equal(12, snapshot.RetainedChunks);
        Assert.Equal(983040, snapshot.RetainedPayloadBytes);
    }
}
