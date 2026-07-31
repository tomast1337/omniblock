using BetaSharp.Diagnostics;
using Hexa.NET.ImGui;

namespace BetaSharp.Client.Diagnostics.Windows;

internal sealed class NetworkInfoWindow : DebugWindow
{
    private readonly FrameGraph _uploadGraph;
    private readonly FrameGraph _downloadGraph;

    private long _lastUploadBytes;
    private long _lastDownloadBytes;

    private readonly Queue<(float Time, long Upload, long Download)> _history = new();
    private float _currentTime;

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

        long uploadDelta = currentUpload - _lastUploadBytes;
        long downloadDelta = currentDownload - _lastDownloadBytes;
        if (uploadDelta < 0) uploadDelta = 0;
        if (downloadDelta < 0) downloadDelta = 0;

        _currentTime += ImGui.GetIO().DeltaTime;
        _history.Enqueue((_currentTime, uploadDelta, downloadDelta));

        long sumUpload = 0;
        long sumDownload = 0;

        while (_history.Count > 0 && _currentTime - _history.Peek().Time > 1.0f)
        {
            _history.Dequeue();
        }

        foreach (var entry in _history)
        {
            sumUpload += entry.Upload;
            sumDownload += entry.Download;
        }

        _uploadGraph.Push(sumUpload);
        _downloadGraph.Push(sumDownload);

        _lastUploadBytes = currentUpload;
        _lastDownloadBytes = currentDownload;

        if (ImGui.CollapsingHeader("Connection statistics", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGuiTextSafe.Text($"Connection Type: {(isInternal ? "Internal Server" : "Remote Server")}");
            if (!isInternal)
            {
                ImGuiTextSafe.Text($"Address: {serverAddress}");
            }
            ImGui.Spacing();
            ImGuiTextSafe.Text($"Total Upload:   {FormatMemory(currentUpload)}");
            ImGuiTextSafe.Text($"Total Download: {FormatMemory(currentDownload)}");
            ImGui.Spacing();
            ImGuiTextSafe.Text($"Upload Packets:   {uploadPackets}");
            ImGuiTextSafe.Text($"Download Packets: {downloadPackets}");
        }

        if (ImGui.CollapsingHeader("Graphs", ImGuiTreeNodeFlags.DefaultOpen))
        {
            _uploadGraph.Draw(40f, 1024 * 2);
            ImGui.Spacing();
            _downloadGraph.Draw(40f, 1024 * 512f);
        }

        DrawPacketArrival(isInternal);
        DrawClockSync(isInternal);
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
