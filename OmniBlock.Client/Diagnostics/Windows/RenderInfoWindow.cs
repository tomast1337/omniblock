using System.Numerics;
using Hexa.NET.ImGui;
using OmniBlock.Diagnostics;
using OmniBlock.Util.Hit;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Client.Diagnostics.Windows;

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
        var hit = ctx.ObjectMouseOver;

        if (hit.Type != HitResultType.Tile || ctx.World == null)
        {
            ImGuiTextSafe.TextDisabled("Not looking at a block.");
            return;
        }

        int x = hit.BlockX, y = hit.BlockY, z = hit.BlockZ;
        var world = ctx.World;

        ImGuiTextSafe.Text($"Pos:   {x}, {y}, {z}");
        ImGuiTextSafe.Text($"Id:    {world.Reader.GetBlockId(x, y, z)}  meta {world.Reader.GetBlockMeta(x, y, z)}");

        // Every face takes its light from the cell it faces, so the neighbors are what decides how
        // this block looks. Its own cell is shown too, but a solid block reads 0/0 there and that is
        // not a fault.
        DrawCell("Self ", world, x, y, z);
        DrawCell("Up   ", world, x, y + 1, z);
        DrawCell("Down ", world, x, y - 1, z);
        DrawCell("X-   ", world, x - 1, y, z);
        DrawCell("X+   ", world, x + 1, y, z);
        DrawCell("Z-   ", world, x, y, z - 1);
        DrawCell("Z+   ", world, x, y, z + 1);

        // Sky light is only meaningful against the height it was computed from: every cell below a
        // column's height is legitimately dark, so a height that disagrees with the terrain reads
        // exactly like a lighting fault.
        if (world.BlockHost.HasChunk(x >> 4, z >> 4))
        {
            ImGuiTextSafe.Text($"Height:  {world.BlockHost.GetChunk(x >> 4, z >> 4).GetHeight(x & 15, z & 15)}");
        }

        if (ctx.ChunkRenderer != null && ctx.ChunkRenderer.TryGetMeshState(x, y, z, out var state, out var hasRenderer))
        {
            ImGuiTextSafe.Text($"Mesh:  epoch {state.Epoch}  meshed {state.LastMeshed}  pending {state.Pending}");
            ImGuiTextSafe.Text($"       renderer {(hasRenderer ? "yes" : "no")}");

            if (state.Epoch != state.LastMeshed && state.Pending == -1)
            {
                ImGuiTextSafe.TextColored(
                    new Vector4(1.0f, 0.4f, 0.4f, 1.0f),
                    "       dirty with nothing queued: the screen is behind the world");
            }
        }
        else
        {
            ImGuiTextSafe.TextDisabled("Mesh:  no version tracked for this sub-chunk");
        }
    }

    /// <summary>One cell: what is in it, whether it stops light, and the light it holds.</summary>
    /// <remarks>
    ///     Opacity is here because it decides both whether the face towards this cell is drawn at
    ///     all and whether light was ever going to reach it. A dark face towards an opaque cell is
    ///     working correctly; a dark face towards a see-through one is not.
    /// </remarks>
    private static void DrawCell(string label, World world, int x, int y, int z)
    {
        var id = world.Reader.GetBlockId(x, y, z);
        var levels = StoredLight(world, x, y, z);
        var opaque = !world.Content.Blocks.AllowsVision(id);

        ImGuiTextSafe.Text($"{label} id {id,3}  {(opaque ? "opaque" : "see-thru")}  sky {levels.Sky,2}  block {levels.Block,2}");
    }

    /// <summary>The two nibbles the chunk actually stores, with nothing derived from them.</summary>
    /// <remarks>
    ///     Not <c>Lighting.GetLightLevels</c>, which answers "what should a face towards this cell
    ///     be shaded by" — for a slab, farmland or stairs that is the brightest of five neighbors
    ///     rather than the cell itself. This view exists to say what the world holds, so it reads
    ///     what the world holds.
    /// </remarks>
    private static LightLevels StoredLight(World world, int x, int y, int z)
    {
        if (y < 0 || y >= ChuckFormat.WorldHeight || !world.BlockHost.HasChunk(x >> 4, z >> 4))
        {
            return default;
        }

        var packed = world.BlockHost.GetChunk(x >> 4, z >> 4).GetPackedLight(x & 15, y, z & 15);
        return LightLevels.Of((packed >> 4) & 0xF, packed & 0xF);
    }

    private void DrawChunkSection()
    {
        ImGuiTextSafe.Text($"Total:    {MetricRegistry.Get(RenderMetrics.ChunksTotal)}");
        ImGuiTextSafe.Text($"Frustum:  {MetricRegistry.Get(RenderMetrics.ChunksFrustum)}");
        ImGuiTextSafe.Text($"Occluded: {MetricRegistry.Get(RenderMetrics.ChunksOccluded)}");
        ImGuiTextSafe.Text($"Rendered: {MetricRegistry.Get(RenderMetrics.ChunksRendered)}");
        ImGuiTextSafe.Text(
            $"Draws:    solid {MetricRegistry.Get(RenderMetrics.SolidDraws)}  translucent {MetricRegistry.Get(RenderMetrics.TranslucentDraws)}");
        ImGuiTextSafe.Text(
            $"Uploads:  geometry {MetricRegistry.Get(RenderMetrics.GeometryUploads)}  light {MetricRegistry.Get(RenderMetrics.LightUploads)}");

        if (ctx.ChunkRenderer is { } chunkRenderer)
        {
            ImGui.Spacing();
            var wireframe = chunkRenderer.WireframeEnabled;
            if (ImGui.Checkbox("Wireframe", ref wireframe))
            {
                chunkRenderer.WireframeEnabled = wireframe;
            }
        }
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
