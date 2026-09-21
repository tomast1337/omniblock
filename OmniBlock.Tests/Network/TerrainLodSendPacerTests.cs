using OmniBlock.Server;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Network;

public sealed class TerrainLodSendPacerTests
{
    [Fact]
    public void Transport_pacer_uses_the_shared_scale_budget()
    {
        Assert.Equal(TerrainLodScaleBudget.TransportBytesPerSecond,
            TerrainLodSendPacer.BytesPerSecond);
        Assert.Equal(TerrainLodScaleBudget.TransportBurstBytes,
            TerrainLodSendPacer.BurstBytes);
        Assert.Equal(TerrainLodScaleBudget.MaximumTransportBacklog,
            TerrainLodSendPacer.MaximumTransportBacklog);
        Assert.Equal(TerrainLodScaleBudget.GlobalTransportBytesPerSecond,
            TerrainLodGlobalSendPacer.BytesPerSecond);
        Assert.Equal(TerrainLodScaleBudget.GlobalTransportBurstBytes,
            TerrainLodGlobalSendPacer.BurstBytes);
        Assert.Equal(TerrainLodScaleBudget.MaximumTransportResponsesPerTick,
            TerrainLodGlobalSendPacer.MaximumResponsesPerTick);
        Assert.Equal(TerrainLodScaleBudget.MaximumQueuedRequestsPerClient,
            TerrainLodRequestQueue.Capacity);
    }

    [Fact]
    public void Gameplay_chunk_queue_always_wins_over_lod_tiles()
    {
        TerrainLodSendPacer pacer = new();

        Assert.False(pacer.TryConsume(
            1024, pendingGameplayChunks: 1, gameplayTransportBacklog: 0, bulkTransportBacklog: 0));
        Assert.False(pacer.TryConsume(
            1024, pendingGameplayChunks: 0, gameplayTransportBacklog: 1, bulkTransportBacklog: 0));
        Assert.True(pacer.TryConsume(
            1024, pendingGameplayChunks: 0, gameplayTransportBacklog: 0, bulkTransportBacklog: 0));
    }

    [Fact]
    public void Transport_pressure_defers_lod_without_spending_tokens()
    {
        TerrainLodSendPacer pacer = new();

        Assert.False(pacer.TryConsume(
            1024, 0, 0, TerrainLodSendPacer.MaximumTransportBacklog));
        Assert.True(pacer.TryConsume(1024, 0, 0, 0));
    }

    [Fact]
    public void Byte_tokens_bound_a_quiet_connection_and_refill_over_time()
    {
        ManualClock clock = new();
        TerrainLodSendPacer pacer = new(clock);

        Assert.True(pacer.TryConsume(TerrainLodSendPacer.BurstBytes, 0, 0, 0));
        Assert.False(pacer.TryConsume(1, 0, 0, 0));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(pacer.TryConsume(TerrainLodSendPacer.BytesPerSecond, 0, 0, 0));
        Assert.False(pacer.TryConsume(1, 0, 0, 0));
    }

    [Fact]
    public void Global_budget_caps_all_clients_and_per_tick_dispatch_work()
    {
        ManualClock clock = new();
        TerrainLodGlobalSendPacer pacer = new(clock);
        pacer.BeginTick();

        for (var i = 0; i < TerrainLodGlobalSendPacer.MaximumResponsesPerTick; i++)
        {
            Assert.True(pacer.CanSend(0));
            pacer.Record(0);
        }
        Assert.False(pacer.CanSend(0));

        pacer.BeginTick();
        Assert.True(pacer.CanSend(TerrainLodGlobalSendPacer.BurstBytes));
        pacer.Record(TerrainLodGlobalSendPacer.BurstBytes);
        Assert.False(pacer.CanSend(1));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(pacer.CanSend(TerrainLodGlobalSendPacer.BytesPerSecond));
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _milliseconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _milliseconds;
        public void Advance(TimeSpan elapsed) => _milliseconds += (long)elapsed.TotalMilliseconds;
    }
}
