using System.Diagnostics;
using OmniBlock.Profiling;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>
/// Render-thread, world-pass totals. Repeated model scopes in the stack profiler retain the last
/// invocation, not the sum, so aggregate here and publish once. GUI/hand/spawner previews outside
/// the world pass do not contribute. CPU submission timings are NOT GPU execution timings.
/// </summary>
internal static class EntityPresentationMetrics
{
    [ThreadStatic] private static bool s_active;
    [ThreadStatic] private static long s_start;
    [ThreadStatic] private static double s_poseMs;
    [ThreadStatic] private static double s_submitMs;
    [ThreadStatic] private static int s_instances, s_draws, s_uploads;
    [ThreadStatic] private static long s_uploadBytes;
    [ThreadStatic] private static Snapshot s_last;

    internal readonly record struct Snapshot(long Serial, double CpuMs, double PoseMs, double SubmitMs,
        int Instances, int DrawBatches, int InstanceUploads, long InstanceUploadBytes);

    public static Snapshot Last => s_last;
    public static long StartTimer() => s_active ? Stopwatch.GetTimestamp() : 0;
    public static void PoseFinished(long start)
    {
        if (s_active && start != 0) s_poseMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    public static void SubmitFinished(long start)
    {
        if (s_active && start != 0) s_submitMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
    public static void Uploaded(int bytes)
    {
        if (!s_active) return;
        s_uploads++;
        s_uploadBytes += bytes;
    }
    public static void Drew(int instances)
    {
        if (!s_active) return;
        s_draws++;
        s_instances += instances;
    }
    public static Pass Begin()
    {
        if (s_active) throw new InvalidOperationException("Nested world entity measurement.");
        s_active = true;
        s_start = Stopwatch.GetTimestamp();
        s_poseMs = s_submitMs = 0;
        s_instances = s_draws = s_uploads = 0;
        s_uploadBytes = 0;
        return new Pass();
    }
    public readonly struct Pass : IDisposable
    {
        public void Dispose()
        {
            s_last = new Snapshot(s_last.Serial + 1, Stopwatch.GetElapsedTime(s_start).TotalMilliseconds,
                s_poseMs, s_submitMs, s_instances, s_draws, s_uploads, s_uploadBytes);
            s_active = false;
            Profiler.Record("EntityPresentationCpu", s_last.CpuMs);
            Profiler.Record("EntityPoseCpu", s_poseMs);
            Profiler.Record("EntityBatchSubmitCpu", s_submitMs);
        }
    }
}
