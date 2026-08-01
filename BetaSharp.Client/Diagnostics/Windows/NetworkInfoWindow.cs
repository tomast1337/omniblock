using BetaSharp.Client.Network;
using BetaSharp.Diagnostics;
using BetaSharp.Network;
using Hexa.NET.ImGui;

namespace BetaSharp.Client.Diagnostics.Windows;

/// <summary>
///     The network overlay, organised by subject rather than by which phase of the rewrite added the
///     panel.
///     <para>
///         It had grown to six sections and around forty lines of scalars, and the ordering recorded
///         the order things were built: totals and per-chunk averages sat together under "Connection
///         statistics" while the graphs of those same totals lived two headers below, and latency was
///         split across "Packet arrival" and "Server clock" — two panels describing one question.
///         Sections here are topics, each holding its own numbers next to its own picture.
///     </para>
///     <para>
///         <b>Distributions are drawn, not tabulated.</b> Five percentiles describe a shape only to
///         someone who already knows what shape to expect, and the two failure modes worth catching —
///         a long thin tail, and a bimodal split — are invisible in them. See
///         <see cref="HistogramView" />.
///     </para>
/// </summary>
internal sealed class NetworkInfoWindow : DebugWindow
{
    private readonly FrameGraph _uploadGraph;
    private readonly FrameGraph _downloadGraph;

    private long _lastUploadBytes;
    private long _lastDownloadBytes;

    private readonly Queue<(float Time, long Upload, long Download, long Processed)> _history = new();
    private float _currentTime;

    private long _lastProcessedPackets;

    public override string Title => "Network Info";

    public NetworkInfoWindow()
    {
        _uploadGraph = new FrameGraph("Upload (B/s)", 240);
        _downloadGraph = new FrameGraph("Download (B/s)", 240);
    }

    protected override void OnDraw()
    {
        bool isInternal = MetricRegistry.Get(ClientMetrics.IsInternal);

        long currentUpload = MetricRegistry.Get(ClientMetrics.UploadBytes);
        long currentDownload = MetricRegistry.Get(ClientMetrics.DownloadBytes);
        long currentProcessed = MetricRegistry.Get(ClientMetrics.PacketsProcessed);

        long uploadDelta = Math.Max(0, currentUpload - _lastUploadBytes);
        long downloadDelta = Math.Max(0, currentDownload - _lastDownloadBytes);
        long processedDelta = Math.Max(0, currentProcessed - _lastProcessedPackets);

        _currentTime += ImGui.GetIO().DeltaTime;
        _history.Enqueue((_currentTime, uploadDelta, downloadDelta, processedDelta));

        while (_history.Count > 0 && _currentTime - _history.Peek().Time > 1.0f)
        {
            _history.Dequeue();
        }

        long sumUpload = 0;
        long sumDownload = 0;
        long sumProcessed = 0;

        foreach ((float _, long upload, long download, long processed) in _history)
        {
            sumUpload += upload;
            sumDownload += download;
            sumProcessed += processed;
        }

        _uploadGraph.Push(sumUpload);
        _downloadGraph.Push(sumDownload);

        _lastUploadBytes = currentUpload;
        _lastDownloadBytes = currentDownload;
        _lastProcessedPackets = currentProcessed;

        DrawStatus(isInternal, sumProcessed);
        DrawThroughput(currentUpload, currentDownload);
        DrawLatency(isInternal);
        DrawWorldData();
        DrawEntityReplication(isInternal);
    }

    /// <summary>
    ///     Who this is connected to, and anything that makes the sections below untrustworthy.
    ///     <para>
    ///         First and open by default because of the second half. A standing read backlog means
    ///         every number under it describes a moment that has already passed rather than the
    ///         connection now, and that failure has no other symptom here: the read thread is
    ///         uncapped and the drain is not, so an overload becomes latency growing without bound
    ///         instead of loss. Nothing is dropped, the packet counters look healthy, and the only
    ///         visible effects are in the game — stale entity positions, a starved interpolation
    ///         buffer, the local player rubber-banding to a correction issued seconds ago.
    ///     </para>
    /// </summary>
    private static void DrawStatus(bool isInternal, long processedPerSecond)
    {
        if (!ImGui.CollapsingHeader("Status", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (isInternal)
        {
            ImGuiTextSafe.Text("Internal server (loopback)");
            ImGuiTextSafe.Text("Packets are handed over as objects: no wire, no");
            ImGuiTextSafe.Text("backlog, no clock offset, no interpolation.");
            return;
        }

        ImGuiTextSafe.Text($"Remote server: {MetricRegistry.Get(ClientMetrics.ServerAddress) ?? "Unknown"}");

        // Zero means the peer never declared one. That is either a vanilla server, which gets no
        // extended packets at all, or a build predating the declaration — the two look the same from
        // here, and both explain a clock that never synchronises.
        long peerProtocol = MetricRegistry.Get(ClientMetrics.PeerProtocolVersion);
        ImGuiTextSafe.Text(peerProtocol > 0
            ? $"Protocol: OmniBlock revision {peerProtocol}"
            : "Protocol: vanilla (no OmniBlock declaration)");

        ImGui.Spacing();

        long depth = MetricRegistry.Get(ClientMetrics.ReadQueueDepth);
        long peak = MetricRegistry.Get(ClientMetrics.ReadQueuePeak);
        long budgetHits = MetricRegistry.Get(ClientMetrics.DrainBudgetHits);

        ImGuiTextSafe.Text($"Read queue: {depth:N0} queued, peak {peak:N0}, {processedPerSecond:N0}/s drained");

        // The one number that says the drain is the constraint rather than the network. Zero means
        // every tick emptied the queue within its budget, whatever the depth reached.
        if (budgetHits > 0)
        {
            ImGuiTextSafe.Text($"  drain budget hit on {budgetHits:N0} ticks (limit {Connection.DrainBudgetMs:F0} ms)");
        }

        // Depth over drain rate is how far behind the game is, in seconds, which is the number that
        // matters — a large queue drained quickly is harmless, a small one drained slowly is not.
        if (depth > 0 && processedPerSecond > 0)
        {
            ImGuiTextSafe.Text($"  behind by {depth / (double)processedPerSecond:F1} s at the current rate");
        }

        if (peak > 1000)
        {
            ImGuiTextSafe.Text("  arriving faster than the drain: everything below is historical");
        }
    }

    private void DrawThroughput(long totalUpload, long totalDownload)
    {
        if (!ImGui.CollapsingHeader("Throughput", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        // Paired on one line rather than stacked in four. Bytes and packets for one direction are
        // read together — the ratio is the interesting part, and it was four lines apart.
        ImGuiTextSafe.Text($"Sent:     {FormatMemory(totalUpload)} in {MetricRegistry.Get(ClientMetrics.UploadPackets):N0} packets");
        ImGuiTextSafe.Text($"Received: {FormatMemory(totalDownload)} in {MetricRegistry.Get(ClientMetrics.DownloadPackets):N0} packets");

        ImGui.Spacing();

        _uploadGraph.Draw(40f, 1024 * 2);
        ImGui.Spacing();
        _downloadGraph.Draw(40f, 1024 * 512f);
    }

    /// <summary>
    ///     Everything about timing, which used to be two panels.
    ///     <para>
    ///         Round-trip time and inter-arrival gap answer the same question from opposite ends —
    ///         how long the link takes, and how evenly it delivers — and the interpolation delay is
    ///         computed from both. Splitting them meant the two halves of one decision were never on
    ///         screen together.
    ///     </para>
    /// </summary>
    private static void DrawLatency(bool isInternal)
    {
        if (!ImGui.CollapsingHeader("Latency", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (isInternal)
        {
            // InternalConnection hands packets straight to the remote handler's queue: it starts no
            // read thread and never calls WritePacket, so there is no gap to sample and no probe to
            // time. Said plainly, because empty histograms here are structural rather than a fault.
            ImGuiTextSafe.Text("Loopback: nothing to measure. Join a remote server.");
            return;
        }

        bool synced = MetricRegistry.Get(ClientMetrics.ClockSynchronised);

        if (synced)
        {
            ImGuiTextSafe.Text($"RTT {MetricRegistry.Get(ClientMetrics.ClockRttMs)} ms median"
                + $"   jitter {MetricRegistry.Get(ClientMetrics.ClockJitterMs)} ms"
                + $"   offset {MetricRegistry.Get(ClientMetrics.ClockOffsetMs):+0;-0;0} ms");
        }
        else
        {
            ImGuiTextSafe.Text("Clock synchronising... (login burst in progress)");
        }

        ImGui.Spacing();
        HistogramView.Draw("Round-trip time", MetricRegistry.Get(ClientMetrics.RttHistogram));

        ImGui.Spacing();
        HistogramView.Draw("Packet arrival gap", MetricRegistry.Get(ClientMetrics.ArrivalHistogram));

        ImGui.Spacing();
        DrawSuggestedDelay(synced);
    }

    /// <summary>
    ///     The delay formula from <c>docs/time-sync-and-interpolation.md</c> §3.4, and the one
    ///     comparison that decides whether its answer is usable on this connection.
    ///     <para>
    ///         Falls back to the arrival p95 as a stand-in for the jitter term before the clock has
    ///         synchronised, which is the only estimate available during the login burst.
    ///     </para>
    /// </summary>
    private static void DrawSuggestedDelay(bool synced)
    {
        double suggested = synced
            ? Math.Clamp(100.0 + (2.0 * MetricRegistry.Get(ClientMetrics.ClockJitterMs)), 100.0, 500.0)
            : Math.Clamp(100.0 + (2.0 * Math.Max(0.0, MetricRegistry.Get(ClientMetrics.ReadIntervalP95Ms) - 50.0)), 100.0, 500.0);

        ImGuiTextSafe.Text($"Suggested interpolation delay: {suggested:F0} ms"
            + (synced ? string.Empty : "  (from arrival p95)"));

        long stamps = MetricRegistry.Get(ClientMetrics.TickStampsReceived);
        if (stamps == 0)
        {
            // Distinguishes an unstamped server from a stalled one. Interpolation falls back to the
            // legacy behaviour here rather than sample against a timeline that does not exist.
            ImGuiTextSafe.Text("Batch stamps: none (server does not stamp)");
            return;
        }

        long age = MetricRegistry.Get(ClientMetrics.TickStampAgeMs);
        ImGuiTextSafe.Text($"Batch stamps: {stamps:N0}, newest {age} ms old");

        // The delay has to exceed the stamp age or the buffer starves every frame. Flagged rather
        // than left to be read off two numbers, because it is the comparison that decides whether
        // the suggestion above is usable at all.
        if (age > suggested)
        {
            ImGuiTextSafe.Text("  age exceeds the suggested delay: the buffer would starve");
        }
    }

    /// <summary>Chunk transfer: the palette encoding and the content-hash cache.</summary>
    private static void DrawWorldData()
    {
        // Zero means chunks are arriving on the inherited path — a vanilla server, or a client whose
        // message registry never negotiated. On loopback that is correct and deliberate: packets are
        // handed over as objects, so compressing one saves bytes that never exist.
        long chunks = MetricRegistry.Get(ClientMetrics.ChunksViaMessage);
        long cached = MetricRegistry.Get(ClientMetrics.ChunksFromCache);

        if (chunks == 0 && cached == 0)
        {
            return;
        }

        if (!ImGui.CollapsingHeader("World data", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (chunks > 0)
        {
            long bytes = MetricRegistry.Get(ClientMetrics.ChunkMessageBytes);
            ImGuiTextSafe.Text($"Chunks sent:   {chunks:N0}   avg {bytes / chunks} B   {FormatMemory(bytes)} total");
        }

        if (cached > 0)
        {
            // The hit rate is the number worth watching: on a first visit it is zero by definition,
            // and on a rejoin to somewhere explored it should dominate.
            ImGuiTextSafe.Text($"Chunks cached: {cached:N0}   {100 * cached / (chunks + cached)}% hit"
                + $"   ~{FormatMemory(cached * WireBytesPerChunk(chunks))} not downloaded");
        }
    }

    /// <summary>
    ///     Entity replication end to end: what arrived, and what the interpolator did with it. One
    ///     section because they are one pipeline — a snapshot rate problem shows up as frozen
    ///     entities, and diagnosing that from two collapsed panels meant opening both.
    /// </summary>
    private static void DrawEntityReplication(bool isInternal)
    {
        if (!ImGui.CollapsingHeader("Entity replication"))
        {
            return;
        }

        if (isInternal)
        {
            ImGuiTextSafe.Text("Loopback: positions are applied directly, with no");
            ImGuiTextSafe.Text("snapshots and no interpolation.");
            return;
        }

        // The per-record average is the number to watch: the four position packets this replaces
        // cost 8 to 10 bytes each, so anything at or above that means the deltas are not landing.
        long records = MetricRegistry.Get(ClientMetrics.SnapshotRecords);
        if (records > 0)
        {
            long bytes = MetricRegistry.Get(ClientMetrics.SnapshotBytes);
            ImGuiTextSafe.Text($"Snapshots: {records:N0} records   avg {bytes / records} B   {FormatMemory(bytes)} total");

            long dropped = MetricRegistry.Get(ClientMetrics.SnapshotsDropped);
            if (dropped > 0)
            {
                ImGuiTextSafe.Text($"  {dropped:N0} dropped: baseline unreachable");
            }
        }
        else
        {
            ImGuiTextSafe.Text("Snapshots: none (server sends position packets)");
        }

        ImGui.Spacing();
        DrawInterpolation();
    }

    /// <summary>
    ///     Render-time interpolation, and the switch to turn it off. Toggling live on one connection
    ///     is the only honest A/B — comparing across two sessions compares two different networks.
    /// </summary>
    private static void DrawInterpolation()
    {
        EntityInterpolator? interpolator = EntityInterpolator.Current;
        if (interpolator is null)
        {
            ImGuiTextSafe.Text("Interpolation: no active connection.");
            return;
        }

        bool enabled = interpolator.Enabled;
        if (ImGui.Checkbox("Interpolate entities", ref enabled))
        {
            interpolator.Enabled = enabled;
        }

        if (!MetricRegistry.Get(ClientMetrics.InterpolationActive))
        {
            // Enabled but inactive means the timeline is missing, not that the switch is off. The
            // two are worth distinguishing here or the checkbox looks broken.
            ImGuiTextSafe.Text(enabled
                ? "  inactive: waiting for a stamped, clock-synced server"
                : "  off: using legacy move-toward-target");
            return;
        }

        long frozen = MetricRegistry.Get(ClientMetrics.InterpolationFrozen);

        // A range: the delay follows each entity's own update rate, so players and dropped items are
        // legitimately rendered at different depths.
        ImGuiTextSafe.Text($"  delay {MetricRegistry.Get(ClientMetrics.InterpolationDelayMs)}"
            + $"-{MetricRegistry.Get(ClientMetrics.InterpolationDelayMaxMs)} ms"
            + $"   {MetricRegistry.Get(ClientMetrics.InterpolationTracked)} tracked");

        ImGuiTextSafe.Text($"  {MetricRegistry.Get(ClientMetrics.InterpolationInterpolated)} interpolated"
            + $"   {MetricRegistry.Get(ClientMetrics.InterpolationExtrapolated)} extrapolated"
            + $"   {frozen} frozen");

        // Entities mid-ramp between two delays. Steady traffic converges to zero, so a number that
        // stays high says the observed update spacing is unstable rather than that anything is wrong
        // with a particular entity.
        long adjusting = MetricRegistry.Get(ClientMetrics.InterpolationAdjusting);

        // Entries into starvation over the session, which is the number §3.5 says to watch. Counted
        // only while the stream as a whole is stale, so it means "the network broke down" and not
        // "some mobs stood still" — the two produce identical per-entity buffers, and an earlier cut
        // of this counted both and read 1180 on a healthy connection.
        long starvations = MetricRegistry.Get(ClientMetrics.InterpolationStarvations);

        if (adjusting > 0 || starvations > 0)
        {
            ImGuiTextSafe.Text($"  {adjusting} adjusting   {starvations} starvations this session");
        }

        if (frozen > 0)
        {
            // The delay scales with each entity's own update rate, so a slow tracking frequency is
            // not a reason to starve. What holds here is an entity the server has stopped sending
            // updates for at all, which for a standing mob is the normal state: EntityTrackerEntry
            // sends nothing until its 400-tick resync. A steady count next to a large interpolated
            // count is a field of idle mobs, not a fault.
            ImGuiTextSafe.Text($"  frozen entities had no update within {EntityInterpolator.MaxDelayMs} ms");
        }
    }

    /// <summary>
    ///     What a chunk would have cost on the wire, for reporting what the cache avoided.
    ///     <para>
    ///         Taken from this session's own sent chunks when there are any, because it varies with
    ///         terrain and is the honest per-connection figure. This used to report the uncompressed
    ///         blob size instead, which overstated the saving roughly sixfold — the blob is what the
    ///         codec produces, not what crosses the wire.
    ///     </para>
    /// </summary>
    /// <param name="sentChunks">Chunks received the normal way, which is the sample.</param>
    private static long WireBytesPerChunk(long sentChunks)
    {
        if (sentChunks > 0)
        {
            return MetricRegistry.Get(ClientMetrics.ChunkMessageBytes) / sentChunks;
        }

        // A fully-cached rejoin sends nothing to average, so fall back to the figure measured over
        // 200 chunks of a real save while sizing the encoding — see docs/network-rewrite.md §5.4.
        return 1966;
    }

    private static string FormatMemory(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F2} KB";
        }

        if (bytes < 1024 * 1024 * 1024)
        {
            return $"{bytes / 1024.0 / 1024.0:F2} MB";
        }

        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }
}
