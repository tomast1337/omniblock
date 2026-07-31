using BetaSharp.Diagnostics;

namespace BetaSharp.Client.Diagnostics;

internal static class ClientMetrics
{
    public static readonly MetricHandle<int> Fps = MetricRegistry.Register<int>("client:fps");
    public static readonly MetricHandle<float> FrameTimeMs = MetricRegistry.Register<float>("client:frame_time_ms");
    public static readonly MetricHandle<long> UploadBytes = MetricRegistry.Register<long>("client:upload_bytes");
    public static readonly MetricHandle<long> DownloadBytes = MetricRegistry.Register<long>("client:download_bytes");
    public static readonly MetricHandle<int> UploadPackets = MetricRegistry.Register<int>("client:upload_packets");
    public static readonly MetricHandle<int> DownloadPackets = MetricRegistry.Register<int>("client:download_packets");
    public static readonly MetricHandle<bool> IsInternal = MetricRegistry.Register<bool>("client:is_internal");
    public static readonly MetricHandle<string> ServerAddress = MetricRegistry.Register<string>("client:server_address");

    // Packet arrival distribution. These size the interpolation delay in
    // docs/time-sync-and-interpolation.md §3.4, which is why p95 is here and not just the mean:
    // the delay is chosen against the typical stall, and the tail is handled by extrapolation.
    public static readonly MetricHandle<double> ReadIntervalMeanMs = MetricRegistry.Register<double>("client:read_interval_mean_ms");
    public static readonly MetricHandle<double> ReadIntervalP50Ms = MetricRegistry.Register<double>("client:read_interval_p50_ms");
    public static readonly MetricHandle<double> ReadIntervalP95Ms = MetricRegistry.Register<double>("client:read_interval_p95_ms");
    public static readonly MetricHandle<double> ReadIntervalP99Ms = MetricRegistry.Register<double>("client:read_interval_p99_ms");
    public static readonly MetricHandle<double> ReadIntervalMaxMs = MetricRegistry.Register<double>("client:read_interval_max_ms");
    public static readonly MetricHandle<long> ReadIntervalSamples = MetricRegistry.Register<long>("client:read_interval_samples");

    // Server clock sync (docs/time-sync-and-interpolation.md phase 2).
    public static readonly MetricHandle<long> ClockOffsetMs = MetricRegistry.Register<long>("client:clock_offset_ms");
    public static readonly MetricHandle<long> ClockRttMs = MetricRegistry.Register<long>("client:clock_rtt_ms");
    public static readonly MetricHandle<long> ClockJitterMs = MetricRegistry.Register<long>("client:clock_jitter_ms");
    public static readonly MetricHandle<bool> ClockSynchronised = MetricRegistry.Register<bool>("client:clock_synchronised");

    static ClientMetrics() { }
}
