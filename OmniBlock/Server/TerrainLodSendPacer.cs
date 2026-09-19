namespace OmniBlock.Server;

/// <summary>
///     A deliberately subordinate byte lane for distant-terrain cache records. Gameplay chunks
///     are allowed to drain first; once they are clear, a token bucket prevents LOD traffic from
///     turning a quiet connection into another unbounded bulk stream.
/// </summary>
public sealed class TerrainLodSendPacer
{
    public const int BytesPerSecond = 256 * 1024;
    public const int BurstBytes = 2 * 1024 * 1024;
    public const int MaximumTransportBacklog = 8;

    private readonly TimeProvider _clock;
    private long _lastRefillTimestamp;
    private double _tokens = BurstBytes;

    public TerrainLodSendPacer(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _lastRefillTimestamp = _clock.GetTimestamp();
    }

    public bool TryConsume(int bytes, int pendingGameplayChunks, int transportBacklog)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        Refill();
        if (pendingGameplayChunks > 0 || transportBacklog >= MaximumTransportBacklog ||
            bytes > _tokens)
            return false;
        _tokens -= bytes;
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
