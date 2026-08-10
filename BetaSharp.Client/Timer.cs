using System.Diagnostics;

namespace OmniBlock.Client;

public class Timer(float tps)
{
    private long _accumulatedSysTime;
    private float _elapsedPartialTicks;
    public int ElapsedTicks;
    private double _lastHrTime;
    private long _lastSyncHrClock = Stopwatch.GetTimestamp();
    private long _lastSyncSysClock = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public float RenderPartialTicks;
    public readonly float TicksPerSecond = tps;
    public readonly float TimerSpeed = 1.0F;
    private double _timeSyncAdjustment = 1.0D;
    public float DeltaTime { get; private set; }

    public void UpdateTimer()
    {
        long currentSysTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long sysDelta = currentSysTime - _lastSyncSysClock;
        long currentHighResTime = Stopwatch.GetTimestamp();
        double currentTimeSeconds = (double)currentHighResTime / Stopwatch.Frequency;
        if (sysDelta is > 1000L or < 0L)
        {
            _lastHrTime = currentTimeSeconds;
        }
        else
        {
            _accumulatedSysTime += sysDelta;
            if (_accumulatedSysTime > 1000L)
            {
                long highResDeltaTicks = currentHighResTime - _lastSyncHrClock;
                double highResDelta = highResDeltaTicks / Stopwatch.Frequency;
                double adjustmentRatio = _accumulatedSysTime / (highResDelta * 1000.0);
                _timeSyncAdjustment += (adjustmentRatio - _timeSyncAdjustment) * 0.2F;
                _lastSyncHrClock = currentHighResTime;
                _accumulatedSysTime = 0L;
            }

            if (_accumulatedSysTime < 0L)
            {
                _lastSyncHrClock = currentHighResTime;
            }
        }

        _lastSyncSysClock = currentSysTime;
        double frameDelta = (currentTimeSeconds - _lastHrTime) * _timeSyncAdjustment;
        DeltaTime = (float)Math.Clamp(frameDelta, 1.0f / 1000.0f, 1.0f);
        _lastHrTime = currentTimeSeconds;

        if (frameDelta is < 0.0D or > 1.0D)
        {
            frameDelta = 0.0D;
        }

        _elapsedPartialTicks = (float)(_elapsedPartialTicks + frameDelta * TimerSpeed * TicksPerSecond);
        ElapsedTicks = (int)_elapsedPartialTicks;
        _elapsedPartialTicks -= ElapsedTicks;
        if (ElapsedTicks > 10)
        {
            ElapsedTicks = 10;
        }

        RenderPartialTicks = _elapsedPartialTicks;
    }
}
