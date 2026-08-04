using BetaSharp.Diagnostics;
using BetaSharp.Util.Hit;
using BetaSharp.Worlds.Core;
using BetaSharp.Worlds.Core.Systems;
using Hexa.NET.ImGui;

namespace BetaSharp.Client.Diagnostics.Windows;

internal sealed class RenderInfoWindow(DebugWindowContext ctx) : DebugWindow
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

        if (ImGui.CollapsingHeader("Targeted Block", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawTargetedBlockSection();
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

    /// <summary>
    ///     What the world holds where you are looking, beside what the mesh for it has been told.
    /// </summary>
    /// <remarks>
    ///     For the case where a block is in the world but not on the screen. The world columns say
    ///     what is there; Epoch past LastMeshed with Pending at -1 says the screen was told and has
    ///     not acted, which is a different fault from the mesh having been built wrongly.
    /// </remarks>
    private void DrawTargetedBlockSection()
    {
        HitResult hit = ctx.ObjectMouseOver;

        if (hit.Type != HitResultType.Tile || ctx.World == null)
        {
            ImGuiTextSafe.TextDisabled("Not looking at a block.");
            return;
        }

        int x = hit.BlockX, y = hit.BlockY, z = hit.BlockZ;
        World world = ctx.World;

        ImGuiTextSafe.Text($"Pos:   {x}, {y}, {z}");
        ImGuiTextSafe.Text($"Id:    {world.Reader.GetBlockId(x, y, z)}  meta {world.Reader.GetBlockMeta(x, y, z)}");

        LightLevels levels = world.Lighting.GetLightLevels(x, y, z, 0);
        ImGuiTextSafe.Text($"Light: sky {levels.Sky}  block {levels.Block}");

        LightLevels above = world.Lighting.GetLightLevels(x, y + 1, z, 0);
        ImGuiTextSafe.Text($"Above: sky {above.Sky}  block {above.Block}");

        if (ctx.ChunkRenderer != null && ctx.ChunkRenderer.TryGetMeshState(x, y, z, out (long Epoch, long LastMeshed, long Pending) state, out bool hasRenderer))
        {
            ImGuiTextSafe.Text($"Mesh:  epoch {state.Epoch}  meshed {state.LastMeshed}  pending {state.Pending}");
            ImGuiTextSafe.Text($"       renderer {(hasRenderer ? "yes" : "no")}");

            if (state.Epoch != state.LastMeshed && state.Pending == -1)
            {
                ImGuiTextSafe.TextColored(
                    new(1.0f, 0.4f, 0.4f, 1.0f),
                    "       dirty with nothing queued: the screen is behind the world");
            }
        }
        else
        {
            ImGuiTextSafe.TextDisabled("Mesh:  no version tracked for this sub-chunk");
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
