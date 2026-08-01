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
    public static readonly MetricHandle<long> PeerProtocolVersion = MetricRegistry.Register<long>("client:peer_protocol_version");

    // Packet arrival distribution. These size the interpolation delay in
    // docs/time-sync-and-interpolation.md §3.4, which is why p95 is here and not just the mean:
    // the delay is chosen against the typical stall, and the tail is handled by extrapolation.
    public static readonly MetricHandle<double> ReadIntervalMeanMs = MetricRegistry.Register<double>("client:read_interval_mean_ms");
    public static readonly MetricHandle<double> ReadIntervalP50Ms = MetricRegistry.Register<double>("client:read_interval_p50_ms");
    public static readonly MetricHandle<double> ReadIntervalP95Ms = MetricRegistry.Register<double>("client:read_interval_p95_ms");
    public static readonly MetricHandle<double> ReadIntervalP99Ms = MetricRegistry.Register<double>("client:read_interval_p99_ms");
    public static readonly MetricHandle<double> ReadIntervalMaxMs = MetricRegistry.Register<double>("client:read_interval_max_ms");
    public static readonly MetricHandle<long> ReadIntervalSamples = MetricRegistry.Register<long>("client:read_interval_samples");

    // Read backlog. Arrival rate is uncapped and the drain is not, so an overload lands here as
    // unbounded latency rather than as loss — which is invisible from every other number on this
    // panel. A rising depth is the cause of stale positions, rubber-banding and a starved
    // interpolation buffer all at once.
    public static readonly MetricHandle<long> ReadQueueDepth = MetricRegistry.Register<long>("client:read_queue_depth");
    public static readonly MetricHandle<long> ReadQueuePeak = MetricRegistry.Register<long>("client:read_queue_peak");
    public static readonly MetricHandle<long> PacketsProcessed = MetricRegistry.Register<long>("client:packets_processed");
    public static readonly MetricHandle<long> DrainBudgetHits = MetricRegistry.Register<long>("client:drain_budget_hits");

    // Server clock sync (docs/time-sync-and-interpolation.md phase 2).
    public static readonly MetricHandle<long> ClockOffsetMs = MetricRegistry.Register<long>("client:clock_offset_ms");
    public static readonly MetricHandle<long> ClockRttMs = MetricRegistry.Register<long>("client:clock_rtt_ms");
    public static readonly MetricHandle<long> ClockJitterMs = MetricRegistry.Register<long>("client:clock_jitter_ms");
    public static readonly MetricHandle<bool> ClockSynchronised = MetricRegistry.Register<bool>("client:clock_synchronised");

    /// <summary>Count of per-tick snapshot stamps received. Zero means the server does not stamp.</summary>
    public static readonly MetricHandle<long> TickStampsReceived = MetricRegistry.Register<long>("client:tick_stamps_received");

    /// <summary>
    ///     Age of the newest snapshot stamp, measured against the client's estimate of server time.
    ///     This is the quantity phase 4's interpolation delay has to cover: render at
    ///     <c>ServerTime - delay</c> and a delay smaller than this age starves the buffer.
    /// </summary>
    public static readonly MetricHandle<long> TickStampAgeMs = MetricRegistry.Register<long>("client:tick_stamp_age_ms");

    /// <summary>Whether remote entities are being driven from snapshots rather than the legacy path.</summary>
    public static readonly MetricHandle<bool> InterpolationActive = MetricRegistry.Register<bool>("client:interp_active");

    public static readonly MetricHandle<long> InterpolationDelayMs = MetricRegistry.Register<long>("client:interp_delay_ms");
    public static readonly MetricHandle<long> InterpolationDelayMaxMs = MetricRegistry.Register<long>("client:interp_delay_max_ms");
    public static readonly MetricHandle<long> InterpolationTracked = MetricRegistry.Register<long>("client:interp_tracked");
    public static readonly MetricHandle<long> InterpolationInterpolated = MetricRegistry.Register<long>("client:interp_interpolated");
    public static readonly MetricHandle<long> InterpolationExtrapolated = MetricRegistry.Register<long>("client:interp_extrapolated");

    /// <summary>Entities past the extrapolation cap. A rising count means the delay is undersized.</summary>
    public static readonly MetricHandle<long> InterpolationFrozen = MetricRegistry.Register<long>("client:interp_frozen");
    public static readonly MetricHandle<long> InterpolationAdjusting = MetricRegistry.Register<long>("client:interp_adjusting");

    /// <summary>
    ///     Cumulative entries into starvation, not entities currently in it. The rising-count metric
    ///     from <c>docs/time-sync-and-interpolation.md</c> §3.5 — a standing Frozen count says some
    ///     entities are idle, whereas this climbing says the buffer is undersized for this link.
    /// </summary>
    public static readonly MetricHandle<long> InterpolationStarvations = MetricRegistry.Register<long>("client:interp_starvations");

    /// <summary>
    ///     Chunks received through <c>ChunkDataMessage</c> — the palette encoding — as opposed to the
    ///     legacy packet. Zero against a vanilla server, and zero on loopback, both by design.
    /// </summary>
    public static readonly MetricHandle<long> ChunksViaMessage = MetricRegistry.Register<long>("client:chunks_via_message");

    /// <summary>Compressed bytes those chunks cost, so the per-chunk average can be read live.</summary>
    public static readonly MetricHandle<long> ChunkMessageBytes = MetricRegistry.Register<long>("client:chunk_message_bytes");

    /// <summary>
    ///     Chunks the server skipped sending because this client already had them. Zero on a first
    ///     visit and expected to dominate on a rejoin.
    /// </summary>
    public static readonly MetricHandle<long> ChunksFromCache = MetricRegistry.Register<long>("client:chunks_from_cache");

    /// <summary>
    ///     Uncompressed blob bytes those chunks would have carried. An over-estimate of the wire
    ///     saving, since a sent chunk is compressed — the honest figure is roughly a sixth of this,
    ///     and both are shown rather than guessed at.
    /// </summary>
    public static readonly MetricHandle<long> ChunkCacheBytesSaved = MetricRegistry.Register<long>("client:chunk_cache_bytes_saved");

    static ClientMetrics() { }
}
