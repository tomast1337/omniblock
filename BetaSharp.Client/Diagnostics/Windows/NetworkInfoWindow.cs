using BetaSharp.Client.Network;
using BetaSharp.Diagnostics;
using BetaSharp.Network;
using Hexa.NET.ImGui;

namespace BetaSharp.Client.Diagnostics.Windows;

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
        string serverAddress = MetricRegistry.Get(ClientMetrics.ServerAddress) ?? "Unknown";
        long currentUpload = MetricRegistry.Get(ClientMetrics.UploadBytes);
        long currentDownload = MetricRegistry.Get(ClientMetrics.DownloadBytes);
        int uploadPackets = MetricRegistry.Get(ClientMetrics.UploadPackets);
        int downloadPackets = MetricRegistry.Get(ClientMetrics.DownloadPackets);

        long currentProcessed = MetricRegistry.Get(ClientMetrics.PacketsProcessed);

        long uploadDelta = currentUpload - _lastUploadBytes;
        long downloadDelta = currentDownload - _lastDownloadBytes;
        long processedDelta = currentProcessed - _lastProcessedPackets;
        if (uploadDelta < 0) uploadDelta = 0;
        if (downloadDelta < 0) downloadDelta = 0;
        if (processedDelta < 0) processedDelta = 0;

        _currentTime += ImGui.GetIO().DeltaTime;
        _history.Enqueue((_currentTime, uploadDelta, downloadDelta, processedDelta));

        long sumUpload = 0;
        long sumDownload = 0;
        long sumProcessed = 0;

        while (_history.Count > 0 && _currentTime - _history.Peek().Time > 1.0f)
        {
            _history.Dequeue();
        }

        foreach (var entry in _history)
        {
            sumUpload += entry.Upload;
            sumDownload += entry.Download;
            sumProcessed += entry.Processed;
        }

        _uploadGraph.Push(sumUpload);
        _downloadGraph.Push(sumDownload);

        _lastUploadBytes = currentUpload;
        _lastDownloadBytes = currentDownload;
        _lastProcessedPackets = currentProcessed;

        // Before everything else: a read backlog makes every number below it a reading of the past
        // rather than of the connection, so it has to be seen first.
        DrawReadBacklog(isInternal, sumProcessed);

        if (ImGui.CollapsingHeader("Connection statistics", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGuiTextSafe.Text($"Connection Type: {(isInternal ? "Internal Server" : "Remote Server")}");
            if (!isInternal)
            {
                ImGuiTextSafe.Text($"Address: {serverAddress}");
            }

            // Zero means the peer never declared one. That is either a vanilla server, which gets no
            // extended packets at all, or a build predating the declaration — the two look the same
            // from here, and both explain a clock that never synchronises.
            long peerProtocol = MetricRegistry.Get(ClientMetrics.PeerProtocolVersion);
            ImGuiTextSafe.Text(peerProtocol > 0
                ? $"Protocol: OmniBlock revision {peerProtocol}"
                : "Protocol: vanilla (no OmniBlock declaration)");

            ImGui.Spacing();
            ImGuiTextSafe.Text($"Total Upload:   {FormatMemory(currentUpload)}");
            ImGuiTextSafe.Text($"Total Download: {FormatMemory(currentDownload)}");
            ImGui.Spacing();
            ImGuiTextSafe.Text($"Upload Packets:   {uploadPackets}");
            ImGuiTextSafe.Text($"Download Packets: {downloadPackets}");

            // Zero means chunks are arriving on the inherited path — a vanilla server, or a client
            // whose message registry never negotiated. On loopback that is correct and deliberate:
            // packets are handed over as objects, so compressing one saves bytes that never exist.
            long chunks = MetricRegistry.Get(ClientMetrics.ChunksViaMessage);
            if (chunks > 0)
            {
                long bytes = MetricRegistry.Get(ClientMetrics.ChunkMessageBytes);

                ImGui.Spacing();
                ImGuiTextSafe.Text($"Chunks (palette): {chunks}");
                ImGuiTextSafe.Text($"  avg {bytes / chunks} B/chunk, {FormatMemory(bytes)} total");
            }
        }

        if (ImGui.CollapsingHeader("Graphs", ImGuiTreeNodeFlags.DefaultOpen))
        {
            _uploadGraph.Draw(40f, 1024 * 2);
            ImGui.Spacing();
            _downloadGraph.Draw(40f, 1024 * 512f);
        }

        DrawPacketArrival(isInternal);
        DrawClockSync(isInternal);
        DrawInterpolation(isInternal);
    }

    /// <summary>
    ///     Packets read off the socket but not yet applied. Drawn first and open by default, because
    ///     a standing backlog invalidates the panels below it: with one, the clock offset, the
    ///     interpolation counts and the arrival percentiles all describe a moment that has already
    ///     passed rather than the connection now.
    ///     <para>
    ///         The failure it catches has no other symptom on this window. The read thread is
    ///         uncapped and the drain is not, so an overload becomes latency growing without bound
    ///         instead of loss — nothing is dropped, the packet counters look healthy, and the only
    ///         visible effects are in the game: stale entity positions, a starved interpolation
    ///         buffer, and the local player rubber-banding to a correction the server issued seconds
    ///         ago.
    ///     </para>
    /// </summary>
    private static void DrawReadBacklog(bool isInternal, long processedPerSecond)
    {
        if (!ImGui.CollapsingHeader("Read backlog", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (isInternal)
        {
            ImGuiTextSafe.Text("Loopback connection: the drain is uncapped,");
            ImGuiTextSafe.Text("so there is no backlog to accumulate.");
            return;
        }

        long depth = MetricRegistry.Get(ClientMetrics.ReadQueueDepth);
        long peak = MetricRegistry.Get(ClientMetrics.ReadQueuePeak);

        long budgetHits = MetricRegistry.Get(ClientMetrics.DrainBudgetHits);

        ImGuiTextSafe.Text($"Queued:    {depth:N0}  (peak {peak:N0})");
        ImGuiTextSafe.Text($"Processed: {processedPerSecond:N0} packets/s");

        // The one number that says the drain is the constraint rather than the network. Zero means
        // every tick emptied the queue within its budget, whatever the depth reached.
        if (budgetHits > 0)
        {
            ImGuiTextSafe.Text($"Budget hit: {budgetHits:N0} ticks (limit {Connection.DrainBudgetMs:F0} ms)");
        }

        if (depth == 0)
        {
            return;
        }

        // Depth over drain rate is how far behind the game is, in seconds, which is the number that
        // matters — a large queue drained quickly is harmless, a small one drained slowly is not.
        if (processedPerSecond > 0)
        {
            ImGuiTextSafe.Text($"Behind by: {depth / (double)processedPerSecond:F1} s at the current rate");
        }

        if (peak > 1000)
        {
            ImGuiTextSafe.Text("Backlog: arriving faster than the drain. Positions");
            ImGuiTextSafe.Text("below are historical, not current.");
        }
    }

    /// <summary>
    ///     Render-time interpolation, and the switch to turn it off. Toggling live on one connection
    ///     is the only honest A/B — comparing across two sessions compares two different networks.
    /// </summary>
    private static void DrawInterpolation(bool isInternal)
    {
        if (!ImGui.CollapsingHeader("Entity interpolation"))
        {
            return;
        }

        if (isInternal)
        {
            ImGuiTextSafe.Text("Loopback connection: entities are not interpolated,");
            ImGuiTextSafe.Text("since there is no transport delay to hide.");
            return;
        }

        EntityInterpolator? interpolator = EntityInterpolator.Current;
        if (interpolator is null)
        {
            ImGuiTextSafe.Text("No active connection.");
            return;
        }

        bool enabled = interpolator.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            interpolator.Enabled = enabled;
        }

        if (!MetricRegistry.Get(ClientMetrics.InterpolationActive))
        {
            // Enabled but inactive means the timeline is missing, not that the switch is off. The
            // two are worth distinguishing here or the checkbox looks broken.
            ImGuiTextSafe.Text(enabled
                ? "Inactive: waiting for a stamped, clock-synced server."
                : "Off: using legacy move-toward-target.");
            return;
        }

        long frozen = MetricRegistry.Get(ClientMetrics.InterpolationFrozen);

        // A range: the delay follows each entity's own update rate, so players and dropped items
        // are legitimately rendered at different depths.
        ImGuiTextSafe.Text($"Delay:        {MetricRegistry.Get(ClientMetrics.InterpolationDelayMs)}"
            + $"-{MetricRegistry.Get(ClientMetrics.InterpolationDelayMaxMs)} ms");
        ImGuiTextSafe.Text($"Tracked:      {MetricRegistry.Get(ClientMetrics.InterpolationTracked)}");
        ImGuiTextSafe.Text($"Interpolated: {MetricRegistry.Get(ClientMetrics.InterpolationInterpolated)}");
        ImGuiTextSafe.Text($"Extrapolated: {MetricRegistry.Get(ClientMetrics.InterpolationExtrapolated)}");
        ImGuiTextSafe.Text($"Frozen:       {frozen}");

        // Entities mid-ramp between two delays. Steady traffic converges to zero, so a number that
        // stays high says the observed update spacing is unstable rather than that anything is wrong
        // with a particular entity.
        long adjusting = MetricRegistry.Get(ClientMetrics.InterpolationAdjusting);
        if (adjusting > 0)
        {
            ImGuiTextSafe.Text($"Adjusting:    {adjusting}");
        }

        // Entries into starvation over the session, which is the number §3.5 says to watch. Counted
        // only while the stream as a whole is stale, so it means "the network broke down" and not
        // "some mobs stood still" — the two produce identical per-entity buffers, and an earlier
        // cut of this counted both and read 1180 on a healthy connection.
        long starvations = MetricRegistry.Get(ClientMetrics.InterpolationStarvations);
        if (starvations > 0)
        {
            ImGuiTextSafe.Text($"Starvations:  {starvations} total");
        }

        if (frozen > 0)
        {
            // The delay scales with each entity's own update rate, so a slow tracking frequency is
            // not a reason to starve. What holds here is an entity the server has stopped sending
            // updates for at all, which for a standing mob is the normal state: EntityTrackerEntry
            // sends nothing until its 400-tick resync. A steady count next to a large Interpolated
            // is a field of idle mobs, not a fault.
            ImGuiTextSafe.Text($"{frozen} holding: no update within the {EntityInterpolator.MaxDelayMs} ms bound.");
        }
    }

    /// <summary>
    ///     Gap between successive packet arrivals. Phase 1 of the interpolation work: the delay is
    ///     sized against p95 rather than the maximum, so both are shown, and the histogram shape
    ///     matters more than either number — a long flat tail means TCP head-of-line blocking, while
    ///     a tight cluster near the 50 ms tick means the stream is keeping up.
    /// </summary>
    private static void DrawPacketArrival(bool isInternal)
    {
        if (!ImGui.CollapsingHeader("Packet arrival"))
        {
            return;
        }

        if (isInternal)
        {
            // InternalConnection hands packets straight to the remote handler's queue: it starts no
            // read thread and never calls WritePacket, so there is no arrival gap to sample. Said
            // plainly, because an empty histogram here is structural rather than a fault.
            ImGuiTextSafe.Text("Loopback connection: packets are handed over directly,");
            ImGuiTextSafe.Text("so there is no transport delay to measure. Join a remote");
            ImGuiTextSafe.Text("server to collect arrival statistics.");
            return;
        }

        long samples = MetricRegistry.Get(ClientMetrics.ReadIntervalSamples);
        if (samples == 0)
        {
            ImGuiTextSafe.Text("No packets received yet.");
            return;
        }

        double p95 = MetricRegistry.Get(ClientMetrics.ReadIntervalP95Ms);

        ImGuiTextSafe.Text($"Samples: {samples:N0}");
        ImGuiTextSafe.Text($"Mean:  {MetricRegistry.Get(ClientMetrics.ReadIntervalMeanMs):F1} ms");
        ImGuiTextSafe.Text($"p50:  <= {MetricRegistry.Get(ClientMetrics.ReadIntervalP50Ms):F0} ms");
        ImGuiTextSafe.Text($"p95:  <= {p95:F0} ms");
        ImGuiTextSafe.Text($"p99:  <= {MetricRegistry.Get(ClientMetrics.ReadIntervalP99Ms):F0} ms");
        ImGuiTextSafe.Text($"Max:     {MetricRegistry.Get(ClientMetrics.ReadIntervalMaxMs):F1} ms");

        ImGui.Spacing();

        // clamp(2 * tickInterval + 2 * jitter, 100, 500), with p95 standing in for the jitter term
        // until the sync handshake supplies a real one. See docs/time-sync-and-interpolation.md §3.4.
        double suggested = Math.Clamp(100.0 + (2.0 * Math.Max(0.0, p95 - 50.0)), 100.0, 500.0);
        ImGuiTextSafe.Text($"Suggested interpolation delay: {suggested:F0} ms");
    }

    /// <summary>
    ///     NTP-style clock synchronisation. Phase 2 of the interpolation work — RTT, offset and
    ///     jitter are visible before anything depends on them, so a session of watching them catches
    ///     surprises before they become bugs.
    /// </summary>
    private static void DrawClockSync(bool isInternal)
    {
        if (!ImGui.CollapsingHeader("Server clock"))
        {
            return;
        }

        if (isInternal)
        {
            ImGuiTextSafe.Text("Loopback connection: offset is identically zero.");
            ImGuiTextSafe.Text("Clock sync runs only against a remote server.");
            return;
        }

        // Stamp arrival is reported before and independently of sync state. The two are separate
        // mechanisms in separate directions, and gating this behind sync hid the fact that stamps
        // were arriving fine while the client's own probes were being dropped.
        DrawStampArrival();

        bool synced = MetricRegistry.Get(ClientMetrics.ClockSynchronised);
        if (!synced)
        {
            ImGuiTextSafe.Text("Synchronising... (login burst in progress)");
            return;
        }

        long offset = MetricRegistry.Get(ClientMetrics.ClockOffsetMs);
        long rtt = MetricRegistry.Get(ClientMetrics.ClockRttMs);
        long jitter = MetricRegistry.Get(ClientMetrics.ClockJitterMs);

        ImGuiTextSafe.Text($"Clock offset: {offset:+0;-0;0} ms  (server minus client)");
        ImGuiTextSafe.Text($"RTT (median):  {rtt} ms");
        ImGuiTextSafe.Text($"Jitter:        {jitter} ms");
        ImGui.Spacing();

        // The interpolation delay formula from docs/time-sync-and-interpolation.md §3.4,
        // now with a real jitter term instead of the p95 stand-in.
        double suggested = Math.Clamp(100.0 + 2.0 * jitter, 100.0, 500.0);
        ImGuiTextSafe.Text($"Suggested interpolation delay: {suggested:F0} ms");

        long age = MetricRegistry.Get(ClientMetrics.TickStampAgeMs);

        // The delay has to exceed the stamp age or the buffer starves every frame. Flagged rather
        // than left to be read off two numbers, because it is the one comparison that decides
        // whether the suggested delay above is usable on this connection.
        if (MetricRegistry.Get(ClientMetrics.TickStampsReceived) > 0 && age > suggested)
        {
            ImGuiTextSafe.Text($"Newest batch age {age} ms exceeds it: buffer would starve.");
        }
    }

    /// <summary>
    ///     Whether the server stamps its entity batches, and how stale the newest one is. Server to
    ///     client, so it works whether or not the client's own clock probes are getting through.
    /// </summary>
    private static void DrawStampArrival()
    {
        long stamps = MetricRegistry.Get(ClientMetrics.TickStampsReceived);
        if (stamps == 0)
        {
            // Distinguishes an unstamped server from a stalled one. Phase 4 must fall back to the
            // legacy behaviour here rather than interpolate against a timeline that does not exist.
            ImGuiTextSafe.Text("Snapshot stamps: none (server does not stamp)");
        }
        else
        {
            ImGuiTextSafe.Text($"Snapshot stamps: {stamps:N0}");
        }

        ImGui.Spacing();
    }

    private static string FormatMemory(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F2} KB";
        if (bytes < 1024 * 1024 * 1024)
            return $"{bytes / 1024.0 / 1024.0:F2} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }
}
