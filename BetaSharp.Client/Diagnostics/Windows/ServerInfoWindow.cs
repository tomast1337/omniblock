using OmniBlock.Diagnostics;
using Hexa.NET.ImGui;

namespace OmniBlock.Client.Diagnostics.Windows;

internal sealed class ServerInfoWindow : DebugWindow
{
    /// <summary>
    ///     How old a reading may be before it counts as absent.
    ///     <para>
    ///         Three times the server's one-second heartbeat, so a single dropped or delayed update
    ///         does not blank the panel. Tight to the heartbeat and every hitch reads as a lost
    ///         connection; far from it and a genuinely lost connection keeps showing the last numbers
    ///         it saw, which is worse than showing nothing.
    ///     </para>
    /// </summary>
    private const double StaleAfterMs = 3000.0;

    private readonly FrameGraph _msptGraph = new("MSPT", 240);

    /// <summary>
    ///     Timestamp of the last MSPT reading pushed into the graph, so redrawing does not push it
    ///     again. This runs once per frame and the value behind it changes once per tick locally and
    ///     once per second remotely; without this the graph is the same handful of numbers repeated
    ///     until they scroll off, which reads as a flat server under load.
    /// </summary>
    private long _lastGraphedAtMs = -1;

    public override string Title => "Server Info";

    protected override void OnDraw()
    {
        // In singleplayer the server writes these directly; on a remote session they arrive as
        // ServerStatusMessage. Either way there is one place to read them from, so this only has to
        // ask whether anything has reported recently.
        if (MetricRegistry.IsStale(ServerMetrics.Tps, StaleAfterMs))
        {
            ImGuiTextSafe.TextDisabled("No report from the server in the last few seconds.");
            ImGui.Separator();
            ImGuiTextSafe.TextDisabled("TPS:      N/A");
            ImGuiTextSafe.TextDisabled("MSPT:     N/A");
            ImGuiTextSafe.TextDisabled("Entities: N/A");
            ImGuiTextSafe.TextDisabled("Players:  N/A");
            return;
        }

        float mspt = MetricRegistry.Get(ServerMetrics.Mspt);

        long msptWrittenAt = MetricRegistry.LastUpdatedMs(ServerMetrics.Mspt);
        if (msptWrittenAt != _lastGraphedAtMs)
        {
            _lastGraphedAtMs = msptWrittenAt;
            _msptGraph.Push(mspt);
        }

        ImGuiTextSafe.Text($"TPS:      {MetricRegistry.Get(ServerMetrics.Tps):F1}");
        ImGuiTextSafe.Text($"MSPT:     {mspt:F2} ms");
        ImGuiTextSafe.Text($"Entities: {MetricRegistry.Get(ServerMetrics.EntityCount)}");
        ImGuiTextSafe.Text($"Players:  {MetricRegistry.Get(ServerMetrics.PlayerCount)}");

        ImGui.Spacing();
        _msptGraph.Draw(40f, 50.0f);
    }
}
