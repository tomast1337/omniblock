using System.Numerics;
using OmniBlock.Client.Entities;
using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Sound;
using OmniBlock.Client.UI;
using OmniBlock.Client.UI.Screens.InGame;
using OmniBlock.Luau;
using OmniBlock.Registries;
using OmniBlock.Server.Worlds;
using OmniBlock.Util.Hit;
using OmniBlock.Worlds.Core;

namespace OmniBlock.Client.Diagnostics;

/// <summary>
///     Aggregates the inputs required by the debug window system so that individual windows
///     are not coupled directly to <see cref="OmniBlock" />.
/// </summary>
/// <remarks>
///     <para>
///         Choose the right data source for each debug window:
///     </para>
///     <list type="bullet">
///         <item>
///             <term>
///                 <see cref="MetricRegistry" />
///             </term>
///             <description>
///                 Use for simple counters or scalars that are written frequently (every frame or every tick)
///                 by hot code paths elsewhere in the engine. The registry is a passive store — reading from
///                 it in <c>OnDraw</c> has no side effects and no allocation.
///             </description>
///         </item>
///         <item>
///             <term><see cref="DebugWindowContext" /> (this class)</term>
///             <description>
///                 Use when you need to call methods, traverse object graphs, or access state that isn't a
///                 simple scalar — e.g. querying the world for biome data, walking the UI element tree, or
///                 reading structured snapshots like <see cref="DebugSystemSnapshot" />.
///             </description>
///         </item>
///     </list>
/// </remarks>
internal sealed class DebugWindowContext(OmniBlock game)
{
    public World? World => game.World;
    public ClientPlayerEntity? Player => game.Player;
    public HitResult ObjectMouseOver => game.ObjectMouseOver;
    public ChunkRenderer? ChunkRenderer => game.WorldRenderer?.ChunkRenderer;
    public ClientTerrainLodSnapshot? TerrainLod => game.WorldRenderer?.TerrainLod?.Snapshot;
    public TerrainLodSpatialSnapshot? TerrainLodSpatial =>
        game.WorldRenderer?.TerrainLod?.SpatialSnapshot;
    public TerrainCoverageSnapshot? TerrainCoverage =>
        game.WorldRenderer?.TerrainLod?.CoverageSnapshot;

    public DebugSystemSnapshot DebugSystemSnapshot => game.DebugSystemSnapshot;
    public UIScreen? CurrentScreen => game.CurrentScreen;
    public HUD HUD => game.HUD;
    public UIContext UIContext => game.UIContext;
    public SoundManager SoundManager => game.SoundManager;
    public TextureManager TextureManager => game.TextureManager;
    public LuauState? LuauState => game.LuauState;
    public string GameDataDir => game.GameDataDir;
    public int? FrameRateLimit => game.Options.MaxFramesPerSecond;
    public bool VSync => game.Options.VSync;
    public ContentRuntime Content => game.Content;
    public WorldGenerationSnapshot? WorldGeneration =>
        game.InternalServer?.worlds?.FirstOrDefault()?.ChunkCache.GenerationTelemetry.Snapshot();

    /// <summary>
    ///     The top-left screen position (in ImGui/window pixels) of the game viewport when the
    ///     debug menu is open, or <see cref="System.Numerics.Vector2.Zero" /> otherwise.
    ///     Used by overlays that draw into the foreground draw list.
    /// </summary>
    public Vector2 DebugViewportScreenPos => game.DebugViewportScreenPos;

    public ulong GetImGuiTextureId(TextureHandle texture) => game.WebGpuRenderer.GetImGuiTextureId(texture);
}
