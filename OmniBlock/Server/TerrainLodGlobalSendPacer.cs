using OmniBlock.Worlds.Lod;

namespace OmniBlock.Server;

/// <summary>
///     Server-wide admission control for distant-terrain replies. Per-client pacers prevent one
///     connection from running away; this shared bucket prevents adding players from multiplying
///     LOD bandwidth without bound. <see cref="BeginTick" /> also caps serialization and dispatch
///     work in one server tick.
/// </summary>
public sealed class TerrainLodGlobalSendPacer
{
    public const int BytesPerSecond = TerrainLodScaleBudget.GlobalTransportBytesPerSecond;
    public const int BurstBytes = TerrainLodScaleBudget.GlobalTransportBurstBytes;
    public const int MaximumResponsesPerTick = TerrainLodScaleBudget.MaximumTransportResponsesPerTick;

    private readonly TimeProvider _clock;
    private long _lastRefillTimestamp;
    private double _tokens = BurstBytes;
    private int _responsesThisTick;

    public TerrainLodGlobalSendPacer(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _lastRefillTimestamp = _clock.GetTimestamp();
    }

    public bool HasResponseCapacity => _responsesThisTick < MaximumResponsesPerTick;

    public void BeginTick()
    {
        _responsesThisTick = 0;
        Refill();
    }

    public bool CanSend(int bytes)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        Refill();
        return HasResponseCapacity && bytes <= _tokens;
    }

    public void Record(int bytes)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        if (!HasResponseCapacity || bytes > _tokens)
            throw new InvalidOperationException("Global terrain LOD transport budget was overspent.");
        _tokens -= bytes;
        _responsesThisTick++;
    }

    private void Refill()
    {
        var now = _clock.GetTimestamp();
        var elapsed = _clock.GetElapsedTime(_lastRefillTimestamp, now).TotalSeconds;
        _lastRefillTimestamp = now;
        if (elapsed > 0)
            _tokens = Math.Min(BurstBytes, _tokens + elapsed * BytesPerSecond);
    }
}
