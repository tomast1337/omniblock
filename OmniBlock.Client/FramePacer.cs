using System.Diagnostics;

namespace OmniBlock.Client;

/// <summary>
///     Caps the complete client frame when VSync is not the limiting mechanism. Most of the wait
///     is yielded to the OS; only the final sub-millisecond interval is spun to avoid accumulating
///     scheduler-quantum overshoot on platforms where <see cref="Thread.Sleep(int)" /> is coarse.
/// </summary>
internal sealed class FramePacer
{
    internal void WaitUntilFrameBudget(long frameStartedAt, int? maxFramesPerSecond)
    {
        if (maxFramesPerSecond is not { } fps) return;

        var target = TargetTimestamp(frameStartedAt, fps, Stopwatch.Frequency);
        while (true)
        {
            var remaining = target - Stopwatch.GetTimestamp();
            if (remaining <= 0) return;

            var remainingMilliseconds = remaining * 1000.0 / Stopwatch.Frequency;
            if (remainingMilliseconds > 2.0)
            {
                Thread.Sleep(Math.Max(1, (int)remainingMilliseconds - 1));
            }
            else
            {
                Thread.SpinWait(64);
            }
        }
    }

    internal static long TargetTimestamp(long frameStartedAt, int maxFramesPerSecond, long frequency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFramesPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frequency);
        return checked(frameStartedAt + (long)Math.Ceiling(frequency / (double)maxFramesPerSecond));
    }
}
