using BetaSharp.Blocks;
using BetaSharp.Diagnostics;
using BetaSharp.Util.Hit;
using BetaSharp.Worlds.Chunks;
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

        // In single player the internal server holds the light this client's copy came from, so
        // disagreeing with it says the light was lost on the way here rather than never computed.
        World? server = ctx.InternalServerWorld;
        if (server != null)
        {
            int serverId = server.Reader.GetBlockId(x, y + 1, z);
            LightLevels here = StoredLight(world, x, y + 1, z);
            LightLevels there = StoredLight(server, x, y + 1, z);

            ImGuiTextSafe.Text($"Up on server:  id {serverId,3}  sky {there.Sky,2}  block {there.Block,2}");

            // Sky light is only meaningful against the height it was computed from. A column whose
            // height the client thinks is higher than it is has every cell below it legitimately
            // dark, and that is a heightmap fault wearing a lighting fault's clothes.
            ImGuiTextSafe.Text($"Height:  client {HeightAt(world, x, z),3}   server {HeightAt(server, x, z),3}");

            // The block first: while a right-click is still unconfirmed the two hold different
            // blocks there, and different blocks are entitled to different light. Reporting that as
            // a light fault sends you looking in the wrong system.
            if (serverId != world.Reader.GetBlockId(x, y + 1, z))
            {
                ImGuiTextSafe.TextColored(
                    new(1.0f, 0.8f, 0.4f, 1.0f),
                    "       client and server disagree about the block above; light cannot be compared");
            }
            else if (here != there)
            {
                ImGuiTextSafe.TextColored(
                    new(1.0f, 0.4f, 0.4f, 1.0f),
                    "       client and server disagree about the light above this block");
            }
        }

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

    /// <summary>One cell: what is in it, whether it stops light, and the light it holds.</summary>
    /// <remarks>
    ///     Opacity is here because it decides both whether the face towards this cell is drawn at
    ///     all and whether light was ever going to reach it. A dark face towards an opaque cell is
    ///     working correctly; a dark face towards a see-through one is not.
    /// </remarks>
    private static void DrawCell(string label, World world, int x, int y, int z)
    {
        int id = world.Reader.GetBlockId(x, y, z);
        LightLevels levels = StoredLight(world, x, y, z);
        bool opaque = !Block.BlocksAllowVision[id];

        ImGuiTextSafe.Text($"{label} id {id,3}  {(opaque ? "opaque" : "see-thru")}  sky {levels.Sky,2}  block {levels.Block,2}");
    }

    /// <summary>The two nibbles the chunk actually stores, with nothing derived from them.</summary>
    /// <remarks>
    ///     Not <c>Lighting.GetLightLevels</c>, which answers "what should a face towards this cell
    ///     be shaded by" — for a slab, farmland or stairs that is the brightest of five neighbors
    ///     rather than the cell itself. Asking that question of two worlds compares two derivations
    ///     instead of two stored values, and the derivations can differ while the storage agrees.
    ///     A view that exists to say whether light arrived has to read what arrived.
    /// </remarks>
    /// <summary>The column height this world believes, or -1 when it holds no chunk there.</summary>
    private static int HeightAt(World world, int x, int z) =>
        world.BlockHost.HasChunk(x >> 4, z >> 4)
            ? world.BlockHost.GetChunk(x >> 4, z >> 4).GetHeight(x & 15, z & 15)
            : -1;

    private static LightLevels StoredLight(World world, int x, int y, int z)
    {
        if (y < 0 || y >= ChuckFormat.WorldHeight || !world.BlockHost.HasChunk(x >> 4, z >> 4))
        {
            return default;
        }

        byte packed = world.BlockHost.GetChunk(x >> 4, z >> 4).GetPackedLight(x & 15, y, z & 15);
        return LightLevels.Of((packed >> 4) & 0xF, packed & 0xF);
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
