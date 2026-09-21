namespace OmniBlock.Server;

using OmniBlock.Worlds.Lod;

/// <summary>
///     A deliberately subordinate byte lane for distant-terrain cache records. Gameplay chunks
///     are allowed to drain first; once they are clear, a token bucket prevents LOD traffic from
///     turning a quiet connection into another unbounded bulk stream.
/// </summary>
public sealed class TerrainLodSendPacer
{
    public const int BytesPerSecond = TerrainLodScaleBudget.TransportBytesPerSecond;
    public const int BurstBytes = TerrainLodScaleBudget.TransportBurstBytes;
    public const int MaximumTransportBacklog = TerrainLodScaleBudget.MaximumTransportBacklog;

    private readonly TimeProvider _clock;
    private long _lastRefillTimestamp;
    private double _tokens = BurstBytes;

    public TerrainLodSendPacer(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _lastRefillTimestamp = _clock.GetTimestamp();
    }

    public bool CanSend(
        int bytes,
        int pendingGameplayChunks,
        int gameplayTransportBacklog,
        int bulkTransportBacklog)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        Refill();
        return pendingGameplayChunks == 0 &&
               gameplayTransportBacklog == 0 &&
               bulkTransportBacklog < MaximumTransportBacklog &&
               bytes <= _tokens;
    }

    public void Record(int bytes)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        if (bytes > _tokens)
            throw new InvalidOperationException("Terrain LOD transport tokens were overspent.");
        _tokens -= bytes;
    }

    public bool TryConsume(
        int bytes,
        int pendingGameplayChunks,
        int gameplayTransportBacklog,
        int bulkTransportBacklog)
    {
        if (!CanSend(bytes, pendingGameplayChunks, gameplayTransportBacklog, bulkTransportBacklog))
            return false;
        Record(bytes);
        return true;
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
