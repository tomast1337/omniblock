using Hexa.NET.ImGui;
using OmniBlock.Diagnostics;

namespace OmniBlock.Client.Diagnostics.Windows;

internal sealed class ClientInfoWindow(DebugWindowContext ctx) : DebugWindow
{
    private readonly FrameGraph _frameTimeGraph = new("Frame Time (ms)", 240);

    public override string Title => "Client Info";

    protected override void OnDraw()
    {
        if (ImGui.CollapsingHeader("Performance", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var frameTimeMs = MetricRegistry.Get(ClientMetrics.FrameTimeMs);
            _frameTimeGraph.Push(frameTimeMs);

            ImGuiTextSafe.Text($"FPS:        {MetricRegistry.Get(ClientMetrics.Fps)}");
            ImGuiTextSafe.Text($"Frame Time: {frameTimeMs:F2} ms");
            ImGui.Spacing();
            _frameTimeGraph.Draw(40f, 0.33f);
        }

        if (ImGui.CollapsingHeader("Memory", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var maxMem = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            var usedMem = Environment.WorkingSet;
            var heapMem = GC.GetTotalMemory(false);

            ImGuiTextSafe.Text($"Used: {FormatMb(usedMem)} / {FormatMb(maxMem)} MB");
            ImGuiTextSafe.Text($"Heap: {FormatMb(heapMem)} MB");
        }

        if (ImGui.CollapsingHeader("World", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var chunkInfo = ctx.World?.GetDebugInfo() ?? "No world loaded.";
            ImGuiTextSafe.Text(chunkInfo);
        }
    }

    private static string FormatMb(long bytes) => bytes > 0 ? $"{bytes / 1024L / 1024L}" : "N/A";
}
