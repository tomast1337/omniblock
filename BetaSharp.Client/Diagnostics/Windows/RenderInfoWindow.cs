using BetaSharp.Diagnostics;
using Hexa.NET.ImGui;

namespace BetaSharp.Client.Diagnostics.Windows;

internal sealed class RenderInfoWindow : DebugWindow
{
    public override string Title => "Render Info";
    public override DebugDock DefaultDock => DebugDock.Right;

    protected override void OnDraw()
    {
        if (MetricRegistry.IsStale(RenderMetrics.ChunksTotal))
        {
            ImGuiTextSafe.TextDisabled("No world loaded.");
            return;
        }

        if (ImGui.CollapsingHeader("Chunks", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawChunkSection();
        }

        if (ImGui.CollapsingHeader("Entities", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawEntitiesSection();
        }

        if (ImGui.CollapsingHeader("Textures", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawTextureSection();
        }
    }

    private static void DrawChunkSection()
    {
        ImGuiTextSafe.Text($"Total:    {MetricRegistry.Get(RenderMetrics.ChunksTotal)}");
        ImGuiTextSafe.Text($"Frustum:  {MetricRegistry.Get(RenderMetrics.ChunksFrustum)}");
        ImGuiTextSafe.Text($"Occluded: {MetricRegistry.Get(RenderMetrics.ChunksOccluded)}");
        ImGuiTextSafe.Text($"Rendered: {MetricRegistry.Get(RenderMetrics.ChunksRendered)}");

        ImGui.Spacing();
        ImGuiTextSafe.Text($"VBO Allocated:      {MetricRegistry.Get(RenderMetrics.VboAllocatedMb):F2} MB");
        ImGuiTextSafe.Text($"Mesh Version Alloc: {MetricRegistry.Get(RenderMetrics.MeshVersionAllocated)}");
        ImGuiTextSafe.Text($"Mesh Version Free:  {MetricRegistry.Get(RenderMetrics.MeshVersionReleased)}");
    }

    private static void DrawEntitiesSection()
    {
        ImGuiTextSafe.Text($"Rendered:  {MetricRegistry.Get(RenderMetrics.EntitiesRendered)}");
        ImGuiTextSafe.Text($"Hidden:    {MetricRegistry.Get(RenderMetrics.EntitiesHidden)}");
        ImGuiTextSafe.Text($"Total:     {MetricRegistry.Get(RenderMetrics.EntitiesTotal)}");
        ImGuiTextSafe.Text($"Particles: {MetricRegistry.Get(RenderMetrics.ParticlesActive)}");
    }

    private static void DrawTextureSection()
    {
        ImGuiTextSafe.Text($"Binds:   {MetricRegistry.Get(RenderMetrics.TextureBindsLastFrame)} (Avg: {MetricRegistry.Get(RenderMetrics.TextureAvgBinds):F1}/f)");
        ImGuiTextSafe.Text($"Active:  {MetricRegistry.Get(RenderMetrics.TextureActive)}");
    }
}
