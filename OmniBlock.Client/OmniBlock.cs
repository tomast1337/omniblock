using System.Diagnostics;
using System.Numerics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.GLFW;
using Microsoft.Extensions.Logging;
using OmniBlock.Client.Diagnostics;
using OmniBlock.Client.DynamicTexture;
using OmniBlock.Client.Entities;
using OmniBlock.Client.Input;
using OmniBlock.Client.Network;
using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering;
using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Client.Rendering.Entities;
using OmniBlock.Client.Rendering.Items;
using OmniBlock.Client.Rendering.UI;
using OmniBlock.Client.Resource;
using OmniBlock.Client.Resource.Pack;
using OmniBlock.Client.Scripting;
using OmniBlock.Client.Sound;
using OmniBlock.Client.UI;
using OmniBlock.Client.UI.Screens;
using OmniBlock.Client.UI.Screens.InGame;
using OmniBlock.Client.UI.Screens.InGame.Containers;
using OmniBlock.Client.UI.Screens.Menu;
using OmniBlock.Client.UI.Screens.Menu.Net;
using OmniBlock.Client.Worlds;
using OmniBlock.Diagnostics;
using OmniBlock.Entities;
using OmniBlock.Luau;
using OmniBlock.Luau.Host;
using OmniBlock.Profiling;
using OmniBlock.Registries;
using OmniBlock.Server.Internal;
using OmniBlock.Server.Worlds;
using OmniBlock.Stats;
using OmniBlock.Util;
using OmniBlock.Util.Hit;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.ClientData.Colors;
using OmniBlock.Worlds.Colors;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Storage;
using OmniBlock.Worlds.Lod;
using Silk.NET.Maths;

namespace OmniBlock.Client;

public partial class OmniBlock :
    IScreenNavigator,
    IControllerState,
    IClientPlayerHost,
    IWorldHost,
    IInternalServerHost,
    ISingleplayerHost
{
    private ContentRuntime? _pendingContent;
    public ContentRuntime Content { get; private set; }

    #region Constants & Static Members

    public static string Version { get; private set; } = UnknownVersion;
    public static string OmniBlockDir => PathHelper.GetAppDir(nameof(OmniBlock));
    public static long HasPaidCheckTime { get; private set; }

    private const string UnknownVersion = "unknown version";
    private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    // Placeholders, not tuned values — docs/luau-persistent-lifecycle-plan.md Open Questions
    // #1/#2 leave both constants for whoever drives real Host-phase call patterns to tune
    // against. Round numbers chosen only so the reset/step calls below are wired to something
    // rather than nothing.
    private const long LuauInstructionBudgetPerTick = 10_000;
    private const int LuauGcStepKb = 16;

    #endregion

    #region Core Game State

    public volatile bool Running = true;
    public volatile bool IsGamePaused;

    public Timer Timer { get; } = new(20.0F);
    public int TicksRan { get; private set; }
    public Session Session { get; private set; }
    public GameOptions Options { get; private set; }

    /// <summary>
    ///     Set from the <c>--debug</c> launch flag, before <see cref="Run" />. Applied to
    ///     <see cref="Options" />' <see cref="GameOptions.ShowDebugInfo" /> as soon as it's
    ///     constructed, in <see cref="SetupDisplay" />, so the debug window is open from the first
    ///     frame instead of waiting for an F3 press.
    /// </summary>
    public bool ForceDebugOnStart { get; set; }

    public IWorldStorageSource SaveLoader { get; private set; }
    public InternalServer? InternalServer { get; private set; }
    public RegistryAccess RegistryAccess { get; private set; } = RegistryAccess.Empty;
    private int? _terrainLodTestHorizonChunks;

    /// <summary>
    ///     The normal option remains capped at the measured public maximum. Explicit E2E launches
    ///     may install a larger per-session policy before opening a world so dormant hierarchy
    ///     levels can pass their release gates without becoming a user-visible setting.
    /// </summary>
    internal int EffectiveTerrainHorizonDistance =>
        _terrainLodTestHorizonChunks ?? Options.TerrainHorizonDistance;

    internal TerrainLodSpatialPolicy TerrainLodPolicy =>
        TerrainLodSpatialPolicy.CreateForMaximumHorizon(
            _terrainLodTestHorizonChunks ??
            TerrainLodSpatialPolicy.MaximumSupportedHorizonChunks);

    #endregion

    #region World & Player Data

    public World? World { get; private set; }
    World? IWorldHost.World => World;
    void IWorldHost.ChangeWorld(World? world) => ChangeWorld(world);

    public ClientPlayerEntity Player { get; private set; }
    public EntityLiving Camera => Player;
    ClientPlayerEntity? IClientPlayerHost.Player => Player;

    public PlayerController PlayerController { get; set; }
    void IClientPlayerHost.SetPlayerController(PlayerController controller) => PlayerController = controller;

    #endregion

    #region Rendering & Display Systems

    public int DisplayWidth { get; private set; }

    public int DisplayHeight { get; private set; }

    /// <summary>
    ///     When the debug viewport is active, the top-left pixel offset of the game viewport
    ///     within the window.
    /// </summary>
    public Vector2 DebugViewportOffset { get; private set; }

    /// <summary>
    ///     The top-left screen position of the game viewport in ImGui/window pixels.
    ///     Zero when the debug menu is closed.
    /// </summary>
    public Vector2 DebugViewportScreenPos => _debugWindowManager?.ViewportPos ?? Vector2.Zero;

    public bool ShowChunkBorders { get; private set; }
    private bool SkipRenderWorld { get; set; }
    public string DebugText { get; private set; } = "";
    public HitResult ObjectMouseOver = new(HitResultType.Miss);

    public GameRenderer GameRenderer { get; private set; }

    /// <summary>Reaches the WebGPU renderer for <see cref="LoadingScreenRenderer" /> and <see cref="LoadScreen" />, both of which draw and present frames of their own outside the main game loop.</summary>
    internal WebGpuGameRenderer WebGpuRenderer { get; private set; }

    public WorldRenderer WorldRenderer { get; private set; }
    public TextureManager TextureManager { get; private set; }
    public SkinManager SkinManager { get; private set; }
    public TextRenderer TextRenderer { get; private set; }
    public UIBatchRenderer UiBatchRenderer { get; private set; }
    public TexturePacks TexturePackList { get; private set; }
    public ParticleManager ParticleManager { get; private set; }

    #endregion

    #region UI & Input Systems

    public UIContext UIContext { get; private set; } = null!;
    public UIScreen? CurrentScreen { get; private set; }
    public HUD HUD { get; private set; } = null!;

    public MouseHelper MouseHelper { get; private set; }
    public VirtualCursor VirtualCursor { get; } = new();
    public bool IsControllerMode { get; set; }
    public int MouseTicksRan { get; set; }
    public bool InGameHasFocus { get; private set; }

    #endregion

    #region Audio & Diagnostics

    public SoundManager SoundManager { get; private set; } = new();
    public StatFileWriter StatFileWriter { get; private set; }

    /// <summary>
    ///     Persistent Luau VM for the UI Host API (docs/luau-ui-host-api-plan.md,
    ///     docs/luau-persistent-lifecycle-plan.md) — null until <c>omniblock_luau</c> is
    ///     resolvable (see <see cref="LuauQuickRun.IsAvailable" />), since the RID-packaged
    ///     NuGet (docs/luau-ffi-embedding-plan.md Part 1.4) hasn't shipped yet and most builds
    ///     of this client won't have the native library. When non-null, its global <c>Host</c>
    ///     and <c>Registry</c> tables have been installed (<see cref="LuauUiHost.Install" />,
    ///     <see cref="LuauRegistryHost.Install" />) and both route to
    ///     <see cref="UiCommandRegistry" />; see <see cref="SetupCoreSystems" />.
    /// </summary>
    public LuauState? LuauState { get; private set; }

    /// <summary>
    ///     This client's UI command table (docs/luau-ui-host-api-plan.md §1). Always constructed,
    ///     even when <see cref="LuauState" /> is null — a mod's Registry-phase module can still
    ///     call <see cref="UI.UiCommandRegistry.Register" /> unconditionally without checking
    ///     whether the native Luau library resolved on this build; it simply won't be reachable
    ///     from any script if it didn't.
    /// </summary>
    public UiCommandRegistry UiCommandRegistry { get; } = new();

    public UiDomDocument UiDomDocument { get; private set; } = null!;

    #endregion

    #region Private Fields

    private readonly ILogger<OmniBlock> _logger = Log.Instance.For<OmniBlock>();
    private readonly ClientLaunchOptions _launchOptions;
    private readonly ClientReadySignal _clientReady = new();
    private readonly E2ETestController? _e2eTestController;
    internal EntityRenderBaseline? EntityBaseline { get; private set; }
    private readonly LoadingScreenRenderer _loadingScreen;
    private readonly WaterSprite _textureWaterFX;
    private readonly LavaSprite _textureLavaFX;
    private readonly DebugTelemetry _debugTelemetry = new();
    private readonly FramePacer _framePacer = new();

    private DebugWindowManager _debugWindowManager;
    private nint _imguiIniFilename;
    private LuauWorldService? _luauWorldService;
    private string? _singleplayerWorldId;
    private bool _luauSchedulerFailed;
    private bool _testDisconnectRequested;

    /// <summary>The directory saves, options and screenshots live under.</summary>
    public string GameDataDir { get; private set; }

    private bool _fullscreen;
    private bool _prevF11Down;
    private bool _prevF3Down;

    private bool _hasCrashed;
    private bool _isTakingScreenshot;

    private int _leftClickCounter;
    private int _tempDisplayWidth;
    private int _tempDisplayHeight;
    private int _joinPlayerCounter;

    private long _prevFrameTime = -1L;
    private long _systemTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private bool _isMainMenuOpen => CurrentScreen is MainMenuScreen;
    private bool _isGameOverOpen => CurrentScreen is GameOverScreen;

    #endregion

    #region Initialization & Lifecycle

    private OmniBlock(int width, int height, bool isFullscreen, ClientLaunchOptions launchOptions, ContentRuntime content)
    {
        Content = content;
        _textureWaterFX = new WaterSprite(content.Blocks);
        _textureLavaFX = new LavaSprite(content.Blocks);
        _launchOptions = launchOptions;
        if (launchOptions.E2ETest is { } e2eTest)
        {
            _e2eTestController = new E2ETestController(e2eTest, () => Running = false);
        }

        ClientReady += RunStartupScript;
        _loadingScreen = new LoadingScreenRenderer(this);
        _tempDisplayHeight = height;
        _fullscreen = isFullscreen;
        DisplayWidth = width;
        DisplayHeight = height;
    }

    public void StartGame()
    {
        LoadVersion();
        Translations.Init();
        MetricRegistry.Bootstrap(typeof(ClientMetrics));
        MetricRegistry.Bootstrap(typeof(RenderMetrics));

        InitializeTimer();

        SetupDisplay();
        SetupCoreSystems();

        // After SetupRenderingAndInput, not before: that's where ImGui.CreateContext() runs, and the
        // WebGPU splash goes through the same ImGuiWgpuBackend/RenderState machinery every other
        // frame does — drawing it any earlier means drawing before that machinery exists.
        SetupRenderingAndInput();

        LoadScreen();

        SetupResourcesAndPostProcessing();

        StatFileWriter.ReadStat(Stats.Stats.StartGameStat, 1);
        Navigate(CreateMainMenuScreen());
        SignalClientReady();
    }

    /// <summary>
    ///     True after resources are loaded and the initial main menu has been initialized.
    /// </summary>
    public bool IsClientReady => _clientReady.IsReady;

    /// <summary>
    ///     Fires at the post-main-menu client-ready boundary. A handler registered after the
    ///     boundary has already been reached runs immediately.
    /// </summary>
    public event Action ClientReady
    {
        add => _clientReady.WhenReady(value);
        remove => _clientReady.Remove(value);
    }

    private void SignalClientReady()
    {
        _logger.LogInformation("Client ready");
        _clientReady.Signal();
    }

    private void RunStartupScript()
    {
        var script = _launchOptions.E2ETest?.Script ?? _launchOptions.StartupScript;
        if (script == null)
        {
            return;
        }

        if (LuauState == null)
        {
            _logger.LogError("Cannot run startup script {Path}: the Luau VM is unavailable", script.Path);
            return;
        }

        // Startup automation is a scheduler task so the script may use OMNI.wait at its top
        // level without blocking rendering or the fixed-tick game loop.
        var scheduledSource = $"OMNI.run(function()\n{script.Source}\nend)";
        LuauState.ResetInstructionBudget(LuauInstructionBudgetPerTick);
        if (!LuauState.TryExecute(scheduledSource, out var error))
        {
            _logger.LogError("Failed to schedule startup script {Path}: {Error}", script.Path, error);
            _e2eTestController?.Fail($"Failed to schedule E2E script: {error}");
            return;
        }

        _logger.LogInformation("Scheduled startup script {Path}", script.Path);
    }

    private unsafe void SetupDisplay()
    {
        var maximumWidth = Display.getDisplayMode().getWidth();
        var maximumHeight = Display.getDisplayMode().getHeight();

        if (_fullscreen)
        {
            Display.setFullscreen(true);
            DisplayWidth = maximumWidth;
            DisplayHeight = maximumHeight;

            if (DisplayWidth <= 0) DisplayWidth = 1;
            if (DisplayHeight <= 0) DisplayHeight = 1;
        }
        else
        {
            Display.setDisplayMode(new DisplayMode(DisplayWidth, DisplayHeight));
            Display.setLocation((maximumWidth - DisplayWidth) / 2, (maximumHeight - DisplayHeight) / 2);
        }

        Display.setTitle($"OmniBlock {Version} ( a BetaSharp fork )");

        GameDataDir = OmniBlockDir;
        SaveLoader = new RegionWorldStorageSource(Path.Combine(GameDataDir, "saves"));
        Options = new GameOptions(this, GameDataDir);
        if (ForceDebugOnStart) Options.ShowDebugInfo = true;
        Options.ReloadTextures += () => { TextureManager.Reload(); };
        Options.ReloadChunks += () => { WorldRenderer.ChunkRenderer.MarkAllVisibleChunksDirty(); };

        Profiler.RegisterMainThread();

        try
        {
            int[] msaaValues = [0, 2, 4, 8];
            Display.MSAA_Samples = msaaValues[Options.MSAALevel];

            Display.create();
            Display.getGlfw().SetWindowSizeLimits(Display.GetWindowHandle(), 850, 480, maximumWidth, maximumHeight);

            // Framebuffer pixels, not window units: on a scaled display the two differ, and
            // everything measured against a render target — the clip rectangles the interface
            // computes above all — is in the former.
            WebGpuDevice.Create(Display.getWindow()!,
                Display.getFramebufferWidth(), Display.getFramebufferHeight(), Options.VSync);
            RenderSystem.Initialize();
            WebGpuRenderer = new WebGpuGameRenderer(this);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception initializing display");
        }
    }

    private void SetupCoreSystems()
    {
        // Must run before EntityRenderDispatcher.Instance below constructs every entity model:
        // each one registers its baked geometry here as it is built, on either backend.
        EntityInstanceBatchRenderer.Initialize(Options);
        EntityRenderDispatcher.Instance.ConfigureContent(Content);

        // Program.cs makes Luau a required client dependency before startup reaches this point.
        // Keep this assertion close to VM construction so alternate hosts that bypass Program's
        // entry point also fail with an actionable message instead of silently disabling scripts.
        if (!LuauQuickRun.IsAvailable())
        {
            _logger.LogCritical(
                "Required Luau runtime is unavailable. Run native/luau/build-local.sh and rebuild the client, or include the RID native package.");
            throw new DllNotFoundException("Required Luau runtime 'omniblock_luau' is unavailable.");
        }

        {
            LuauState = new LuauState();

            UiDomDocument = new UiDomDocument(() => CurrentScreen?.Root, () => HUD?.Root);
            LuauDomHost.Query = UiDomDocument.Query;
            LuauDomHost.Parent = UiDomDocument.GetParent;
            LuauDomHost.ChildCount = UiDomDocument.GetChildCount;
            LuauDomHost.Child = UiDomDocument.GetChild;
            LuauDomHost.GetString = UiDomDocument.GetString;
            LuauDomHost.SetString = UiDomDocument.SetString;
            LuauDomHost.GetBool = UiDomDocument.GetBool;
            LuauDomHost.SetBool = UiDomDocument.SetBool;
            LuauDomHost.Click = UiDomDocument.Click;
            LuauDomHost.Screen = () => CurrentScreen?.GetType().Name;
            LuauDomHost.Install(LuauState.Handle);
            LuauLogHost.WriteLine = message => Log.Instance.For("Luau").LogInformation("{Message}", message);
            LuauLogHost.Install(LuauState.Handle);
            LuauState.ResetInstructionBudget(LuauInstructionBudgetPerTick);
            if (!LuauState.TryExecute(LuauDomHost.Bootstrap, out var domBootstrapError))
            {
                _logger.LogError("Failed to install the Luau DOM bootstrap: {Error}", domBootstrapError);
            }

            if (!LuauState.TryExecute(LuauScheduler.Bootstrap, out var schedulerBootstrapError))
            {
                _luauSchedulerFailed = true;
                _logger.LogError("Failed to install the Luau scheduler bootstrap: {Error}", schedulerBootstrapError);
            }

            LuauConfigHost.Get = Options.GetScriptConfig;
            LuauConfigHost.Set = Options.SetScriptConfig;
            LuauConfigHost.Options = Options.GetScriptConfigOptions;
            LuauConfigHost.Install(LuauState.Handle);
            if (!LuauState.TryExecute(LuauConfigHost.Bootstrap, out var configBootstrapError))
            {
                _logger.LogError("Failed to install the Luau configuration bootstrap: {Error}", configBootstrapError);
            }

            LuauClientStateHost.WorldLoaded = () => World != null;
            LuauClientStateHost.PlayerReady = () =>
                World != null && Player != null &&
                CurrentScreen is not (LevelLoadingScreen or ConnectingScreen or DownloadingTerrainScreen);
            LuauClientStateHost.WorldId = () => World != null && InternalServer != null ? _singleplayerWorldId : null;
            LuauClientStateHost.DebugOpen = () => Options.ShowDebugInfo;
            LuauClientStateHost.MeshPending = () => WorldRenderer?.ChunkRenderer.PendingMeshWork ?? 0;
            LuauClientStateHost.MeshSupersededCount = () => WorldRenderer?.ChunkRenderer.MeshLifecycle.Superseded ?? 0;
            LuauClientStateHost.MeshBuildFailureCount = () => WorldRenderer?.ChunkRenderer.MeshLifecycle.BuildFailures ?? 0;
            LuauClientStateHost.MeshAwaitingUpload = () => WorldRenderer?.ChunkRenderer.MeshLifecycle.AwaitingUpload ?? 0;
            LuauClientStateHost.MeshAwaitingDraw = () => WorldRenderer?.ChunkRenderer.MeshLifecycle.AwaitingDraw ?? 0;
            LuauClientStateHost.MeshLeadingEdgeQueued = () => WorldRenderer?.ChunkRenderer.LeadingEdgeQueued ?? 0;
            LuauClientStateHost.MeshLeadingEdgePending = () => WorldRenderer?.ChunkRenderer.LeadingEdgePending ?? 0;
            LuauClientStateHost.MeshEvictionGraceCount = () => WorldRenderer?.ChunkRenderer.EvictionGraceMeshCount ?? 0;
            LuauClientStateHost.MeshCooperativeCancellationCount = () => WorldRenderer?.ChunkRenderer.MeshLifecycle.CooperativeCancellations ?? 0;
            LuauClientStateHost.MeshCriticalCompletedCount = () => WorldRenderer?.ChunkRenderer.MeshLifecycle.CriticalCompleted ?? 0;
            LuauClientStateHost.MeshCriticalDeadlineMissCount = () => WorldRenderer?.ChunkRenderer.MeshLifecycle.CriticalDeadlineMisses ?? 0;
            LuauClientStateHost.MeshCriticalOverdueCount = () => WorldRenderer?.ChunkRenderer.MeshLifecycle.CriticalOverdue ?? 0;
            LuauClientStateHost.MeshCancelledCount = () => WorldRenderer?.ChunkRenderer.MeshLifecycle.Cancelled ?? 0;
            LuauClientStateHost.MeshRequestToGpuMs = () => WorldRenderer?.ChunkRenderer.MeshProfile.RequestToUploadMs ?? 0;
            LuauClientStateHost.FrameTimeMs = () => MetricRegistry.Get(ClientMetrics.FrameTimeMs);
            LuauClientStateHost.MeshSafetyLoadedColumns = () => CurrentMeshSafetyRingState().LoadedColumns;
            LuauClientStateHost.MeshSafetyExpectedSections = () => CurrentMeshSafetyRingState().ExpectedSections;
            LuauClientStateHost.MeshSafetyHoles = () => CurrentMeshSafetyRingState().MissingMeshes;
            LuauClientStateHost.MeshReadyRadius = () => WorldRenderer?.ChunkRenderer.MeshReadyRadius ?? 0;
            LuauClientStateHost.ResidentMeshCount = () => WorldRenderer?.ChunkRenderer.ResidentMeshCount ?? 0;
            LuauClientStateHost.PresentedMeshCount = () => WorldRenderer?.ChunkRenderer.PresentedMeshCount ?? 0;
            LuauClientStateHost.ForegroundPending = () => WorldRenderer?.ChunkRenderer.ForegroundPending ?? 0;
            LuauClientStateHost.BackgroundPending = () => WorldRenderer?.ChunkRenderer.BackgroundPending ?? 0;
            LuauClientStateHost.LightRefreshPending = () => WorldRenderer?.ChunkRenderer.LightRefreshPending ?? 0;
            LuauClientStateHost.LightRefreshCompletedCount = () =>
                WorldRenderer?.ChunkRenderer.LightRefreshCompletedCount ?? 0;
            LuauClientStateHost.GeometryUploadsLastFrame = () =>
                WorldRenderer?.ChunkRenderer.GeometryUploadsLastFrame ?? 0;
            LuauClientStateHost.LightUploadsLastFrame = () =>
                WorldRenderer?.ChunkRenderer.LightUploadsLastFrame ?? 0;
            LuauClientStateHost.SolidDrawsLastFrame = () =>
                WorldRenderer?.ChunkRenderer.SolidDrawsLastFrame ?? 0;
            LuauClientStateHost.TranslucentDrawsLastFrame = () =>
                WorldRenderer?.ChunkRenderer.TranslucentDrawsLastFrame ?? 0;
            LuauClientStateHost.ResidentSolidLayerCount = () =>
                WorldRenderer?.ChunkRenderer.PresentationProfile.ResidentSolidLayers ?? 0;
            LuauClientStateHost.ResidentTranslucentLayerCount = () =>
                WorldRenderer?.ChunkRenderer.PresentationProfile.ResidentTranslucentLayers ?? 0;
            LuauClientStateHost.VisibilityCandidates = () =>
                WorldRenderer?.ChunkRenderer.PresentationProfile.VisibilityCandidates ?? 0;
            LuauClientStateHost.VisibilityReuseFrames = () =>
                WorldRenderer?.ChunkRenderer.VisibilityReuseProfile.ReusedFrames ?? 0;
            LuauClientStateHost.VisibilitySynchronousFrames = () =>
                WorldRenderer?.ChunkRenderer.VisibilityReuseProfile.SynchronousFrames ?? 0;
            LuauClientStateHost.VisibilityBuilds = () =>
                WorldRenderer?.ChunkRenderer.VisibilityReuseProfile.Builds ?? 0;
            LuauClientStateHost.VisibilityBuildCancellations = () =>
                WorldRenderer?.ChunkRenderer.VisibilityReuseProfile.Cancellations ?? 0;
            LuauClientStateHost.VisibilityStaleResults = () =>
                WorldRenderer?.ChunkRenderer.VisibilityReuseProfile.StaleResults ?? 0;
            LuauClientStateHost.VisibilityBuildInFlight = () =>
                WorldRenderer?.ChunkRenderer.VisibilityReuseProfile.BuildInFlight == true ? 1 : 0;
            LuauClientStateHost.VisibilityWorkerCandidates = () =>
                WorldRenderer?.ChunkRenderer.VisibilityReuseProfile.ConservativeCandidates ?? 0;
            LuauClientStateHost.FrustumTests = () =>
                WorldRenderer?.ChunkRenderer.PresentationProfile.FrustumTests ?? 0;
            LuauClientStateHost.PortalVisited = () =>
                WorldRenderer?.ChunkRenderer.PresentationProfile.PortalVisited ?? 0;
            LuauClientStateHost.SafetyRescued = () =>
                WorldRenderer?.ChunkRenderer.PresentationProfile.SafetyRescued ?? 0;
            LuauClientStateHost.RenderDistance = () =>
                World is ClientWorld renderWorld && renderWorld.NetworkHandler.ServerRenderDistance > 0
                    ? renderWorld.NetworkHandler.ServerRenderDistance
                    : Options.RenderDistance;
            LuauClientStateHost.SimulationDistance = () =>
                World is ClientWorld simulationWorld && simulationWorld.NetworkHandler.ServerSimulationDistance > 0
                    ? simulationWorld.NetworkHandler.ServerSimulationDistance
                    : Options.SimulationDistance;
            LuauClientStateHost.PresentedSolidLayerCount = () =>
                WorldRenderer?.ChunkRenderer.PresentationProfile.PresentedSolidLayers ?? 0;
            LuauClientStateHost.PresentedTranslucentLayerCount = () =>
                WorldRenderer?.ChunkRenderer.PresentationProfile.PresentedTranslucentLayers ?? 0;
            LuauClientStateHost.EmptyLayersSubmitted = () =>
                WorldRenderer?.ChunkRenderer.PresentationProfile.EmptyLayersSubmitted ?? 0;
            LuauClientStateHost.TerrainDrawCalls = () =>
                WorldRenderer?.ChunkRenderer.PresentationProfile.TerrainDrawCalls ?? 0;
            LuauClientStateHost.TerrainUniformEntries = () =>
                WorldRenderer?.ChunkRenderer.PresentationProfile.TerrainUniformEntries ?? 0;
            LuauClientStateHost.TerrainSubmissionBatches = () =>
                WorldRenderer?.ChunkRenderer.PresentationProfile.TerrainSubmissionBatches ?? 0;
            LuauClientStateHost.TerrainPipelineBinds = () =>
                WorldRenderer?.ChunkRenderer.PresentationProfile.TerrainPipelineBinds ?? 0;
            LuauClientStateHost.TerrainTextureBinds = () =>
                WorldRenderer?.ChunkRenderer.PresentationProfile.TerrainTextureBinds ?? 0;
            LuauClientStateHost.TerrainUniformArenaCapacity = () =>
                WorldRenderer?.ChunkRenderer.PresentationProfile.TerrainUniformArenaCapacity ?? 0;
            LuauClientStateHost.TerrainUniformArenaGrowths = () =>
                WorldRenderer?.ChunkRenderer.PresentationProfile.TerrainUniformArenaGrowths ?? 0;
            LuauClientStateHost.FindVisibleMs = () =>
                WorldRenderer?.ChunkRenderer.PresentationProfile.FindVisible.LastMs ?? 0;
            LuauClientStateHost.TerrainSubmitCpuMs = () =>
                WorldRenderer?.ChunkRenderer.PresentationProfile.TerrainSubmit.LastMs ?? 0;
            LuauClientStateHost.TerrainLodMetric = key =>
            {
                var state = WorldRenderer?.TerrainLod?.Snapshot ?? default;
                var spatial = WorldRenderer?.TerrainLod?.SpatialSnapshot ?? default;
                var coverage = WorldRenderer?.TerrainLod?.CoverageSnapshot ?? default;
                var terrainNetwork = (World as ClientWorld)?.NetworkHandler;
                return key switch
                {
                    "terrainLodPending" => state.PendingColumns,
                    "terrainLodConverting" => state.ConversionOwnedColumns,
                    "terrainLodResident" => state.ResidentColumns,
                    "terrainLodLevel0Resident" => state.ExactVoxelLevelColumns,
                    "terrainLodLevel1Resident" => state.TransitionLevelColumns,
                    "terrainLodPresented" => state.PresentedColumns,
                    "terrainLodTranslucentPresented" => state.PresentedTranslucentColumns,
                    "terrainLodHandoffPreparing" => state.HandoffPreparingColumns,
                    "terrainLodHandoffOverlap" => state.HandoffOverlapColumns,
                    "terrainLodHandoffsStarted" => state.HandoffsStarted,
                    "terrainLodHandoffReversals" => state.HandoffReversals,
                    "terrainLodLevelTransitions" => state.LevelTransitionColumns,
                    "terrainLodLevelTransitionsStarted" => state.LevelTransitionsStarted,
                    "terrainLodLevelTransitionReversals" => state.LevelTransitionReversals,
                    "terrainLodBoundaryLinked" => state.BoundaryLinkedColumns,
                    "terrainLodBoundaryPending" => state.BoundaryPendingColumns,
                    "terrainLodBoundaryRefreshes" => state.BoundaryRefreshes,
                    "terrainLodBoundaryBytes" => state.ResidentBoundaryBytes,
                    "terrainLodUploads" => state.UploadsThisFrame,
                    "terrainLodMeshOwned" => state.MeshCompilationOwned,
                    "terrainLodMeshCoverageQueued" => state.MeshCoverageQueued,
                    "terrainLodMeshRefinementQueued" => state.MeshRefinementQueued,
                    "terrainLodMeshCoverageCompleted" => state.MeshCoverageCompleted,
                    "terrainLodMeshRefinementCompleted" => state.MeshRefinementCompleted,
                    "terrainLodMeshCompletedBytes" => state.MeshCompletedResultBytes,
                    "terrainLodMeshPredictedBytes" => state.MeshPredictedResultBytes,
                    "terrainLodMeshPredictedMs" => state.MeshPredictedCompilationMs,
                    "terrainLodMeshAdmissionDeferrals" => state.MeshAdmissionDeferrals,
                    "terrainLodMeshUploadDeferrals" => state.MeshUploadAdmissionDeferrals,
                    "terrainLodMeshOversizedUploads" => state.MeshOversizedUploadAdmissions,
                    "terrainLodMeshCompilationSamples" => state.MeshCompilationSamples,
                    "terrainLodMeshUploadSamples" => state.MeshUploadSamples,
                    "terrainLodMeshCompilationMsPerKCell" => state.MeshCompilationMsPerKCell,
                    "terrainLodMeshResultBytesPerKCell" => state.MeshResultBytesPerKCell,
                    "terrainLodMeshUploadBaseMs" => state.MeshUploadBaseMs,
                    "terrainLodMeshUploadMsPerMiB" => state.MeshUploadMsPerMiB,
                    "terrainLodGpuBytes" => state.ResidentGpuBytes,
                    "terrainLodStaleResults" => state.StaleResults,
                    "terrainLodRejected" => state.RejectedAdmissions,
                    "terrainLodEvictions" => state.Evictions,
                    "terrainLodCacheHits" => state.CacheHits,
                    "terrainLodCacheMisses" => state.CacheMisses,
                    "terrainLodResourceGeneration" => state.ResourceGeneration,
                    "terrainLodResourceReloads" => state.ResourceReloads,
                    "terrainLodResourceReusedColumns" => state.LastResourceReloadReusedColumns,
                    "terrainLodResourceReusedGpuBytes" => state.LastResourceReloadReusedGpuBytes,
                    "terrainLodSolidCpuMs" => state.SolidRenderCpuMs,
                    "terrainLodTranslucentCpuMs" => state.TranslucentRenderCpuMs,
                    "terrainLodCacheBytes" => state.CacheBytes,
                    "terrainLodSpatialComplete" => spatial.CompleteCoverage ? 1 : 0,
                    "terrainLodSpatialSelected" => spatial.SelectedTiles,
                    "terrainLodSpatialParentFallbacks" => spatial.ParentFallbacks,
                    "terrainLodSpatialMissingGroups" => spatial.MissingCoverageGroups,
                    "terrainLodSpatialGpuResident" => spatial.GpuPresentations,
                    "terrainLodSpatialHighestGpuLevel" => spatial.HighestGpuResidentLevel,
                    "terrainLodSpatialCpuTiles" => spatial.Hierarchy.Tiles,
                    "terrainLodSpatialCurrentTiles" => spatial.Hierarchy.CurrentTiles,
                    "terrainLodSpatialMeshPending" => spatial.PendingMeshCandidates,
                    "terrainLodSpatialMeshQueued" => spatial.MeshCompilation.Queued,
                    "terrainLodSpatialMeshRunning" => spatial.MeshCompilation.Running,
                    "terrainLodSpatialMeshReady" => spatial.MeshCompilation.Ready,
                    "terrainLodSpatialMeshCancelled" => spatial.MeshCompilation.Cancelled,
                    "terrainLodSpatialMeshOverBudget" => spatial.MeshCompilation.OverBudget,
                    "terrainLodSpatialMeshOldestQueuedMs" =>
                        spatial.MeshCompilation.OldestQueuedMs,
                    "terrainLodSpatialMeshCompleted" => spatial.MeshCompilation.Completed,
                    "terrainLodSpatialMeshCompilationTotalMs" =>
                        spatial.MeshCompilation.TotalCompilationMs,
                    "terrainLodSpatialMeshCompilationMaxMs" =>
                        spatial.MeshCompilation.MaximumCompilationMs,
                    "terrainLodSpatialMeshPeakQueued" => spatial.MeshCompilation.PeakQueued,
                    "terrainLodSpatialMeshPeakRunning" => spatial.MeshCompilation.PeakRunning,
                    "terrainLodSpatialMeshPeakCompleted" =>
                        spatial.MeshCompilation.PeakCompleted,
                    "terrainLodSpatialMeshReductionMs" =>
                        spatial.MeshCompilation.ReductionMs,
                    "terrainLodSpatialMeshFaceEmissionMs" =>
                        spatial.MeshCompilation.FaceEmissionMs,
                    "terrainLodSpatialMeshFlatteningMs" =>
                        spatial.MeshCompilation.FlatteningMs,
                    "terrainLodSpatialMeshCoalescingMs" =>
                        spatial.MeshCompilation.CoalescingMs,
                    "terrainLodSpatialMeshSourceColumns" =>
                        spatial.MeshCompilation.SourceColumns,
                    "terrainLodSpatialMeshSourceSpans" => spatial.MeshCompilation.SourceSpans,
                    "terrainLodSpatialMeshConstructionPages" =>
                        spatial.MeshCompilation.ConstructionPages,
                    "terrainLodSpatialSeamDesired" => spatial.DesiredSeams,
                    "terrainLodSpatialSeamGpuResident" => spatial.GpuSeams,
                    "terrainLodSpatialSeamQueued" => spatial.SeamCompilation.Queued,
                    "terrainLodSpatialSeamRunning" => spatial.SeamCompilation.Running,
                    "terrainLodSpatialSeamReady" => spatial.SeamCompilation.Ready,
                    "terrainLodSpatialSeamCancelled" => spatial.SeamCompilation.Cancelled,
                    "terrainLodSpatialSeamOverBudget" => spatial.SeamCompilation.OverBudget,
                    "terrainLodSpatialSeamOldestQueuedMs" =>
                        spatial.SeamCompilation.OldestQueuedMs,
                    "terrainLodSpatialSubmissionReady" => spatial.SubmissionReady ? 1 : 0,
                    "terrainLodSpatialAuthoritativeTiles" => spatial.AuthoritativeTiles,
                    "terrainLodSpatialHighestAuthoritativeLevel" =>
                        spatial.HighestAuthoritativeLevel,
                    "terrainLodSpatialSolidPages" => spatial.SubmittedSolidPages,
                    "terrainLodSpatialTranslucentPages" => spatial.SubmittedTranslucentPages,
                    "terrainLodSpatialGpuBytes" => spatial.GpuBytes,
                    "terrainLodSpatialPinned" => spatial.PinnedPresentations,
                    "terrainLodSpatialGpuEvictions" => spatial.GpuEvictions,
                    "terrainLodSpatialCpuEvictions" => spatial.CpuEvictions,
                    "terrainCoverageReady" => coverage.IsComplete ? 1 : 0,
                    "terrainCoverageExpected" => coverage.ExpectedColumns,
                    "terrainCoverageCovered" => coverage.CoveredColumns,
                    "terrainCoverageExact" => coverage.ExactOwnedColumns,
                    "terrainCoverageColumnLod" => coverage.ColumnLodOwnedColumns,
                    "terrainCoverageSpatial" => coverage.SpatialOwnedColumns,
                    "terrainCoverageTransitions" => coverage.TransitionColumns,
                    "terrainCoverageHoles" => coverage.HoleCount,
                    "terrainCoverageOverlaps" => coverage.OverlapCount,
                    "terrainCoverageExpectedSeams" => coverage.ExpectedSeams,
                    "terrainCoverageMissingSeams" => coverage.MissingSeams,
                    "terrainCoveragePendingSeams" => coverage.PendingReplacementSeams,
                    "terrainCoverageUnexpectedSeams" => coverage.UnexpectedSeams,
                    "terrainCoverageFailureKind" => (int)coverage.FirstFailureKind,
                    "terrainCoverageFailureX" => coverage.FirstFailureX,
                    "terrainCoverageFailureZ" => coverage.FirstFailureZ,
                    "terrainLodRemoteRequests" => state.RemoteRequests,
                    "terrainLodRemoteTiles" => state.RemoteTiles,
                    "terrainLodRemoteBytes" => state.RemoteWireBytes,
                    "terrainLodRemotePending" => state.RemotePendingResponses,
                    "terrainLodRemoteMissing" => state.RemoteMissingResponses,
                    "terrainLodRemoteDeferred" => state.RemoteDeferredResponses,
                    "terrainLodRemoteCoverageRequired" => state.RemoteCoverageRequired,
                    "terrainLodRemoteCoverageAvailable" => state.RemoteCoverageAvailable,
                    "terrainLodRemoteCoverageInFlight" => state.RemoteCoverageInFlight,
                    "terrainLodRemoteCoveragePending" => state.RemoteCoveragePending,
                    "terrainLodRemoteCoverageMissing" => state.RemoteCoverageMissing,
                    "terrainLodRemoteCoverageDeferred" => state.RemoteCoverageDeferred,
                    "terrainLodRemoteCoverageComplete" =>
                        state.RemoteCoverageRequired > 0 &&
                        state.RemoteCoverageAvailable == state.RemoteCoverageRequired ? 1 : 0,
                    "terrainLodNetworkTilesReceived" =>
                        state.NetworkTilesReceived,
                    "terrainLodNetworkTilesAdmitted" =>
                        state.NetworkTilesAdmitted,
                    "terrainLodNetworkTileQueue" =>
                        state.NetworkTileQueueDepth,
                    "terrainLodNetworkTileQueuePeak" =>
                        state.NetworkTileQueuePeak,
                    "terrainLodTransportQueue" =>
                        state.TransportQueueDepth,
                    "terrainLodTransportQueuePeak" =>
                        state.TransportQueuePeak,
                    "terrainLodCoarseSourceUnavailable" => state.CoarseCoverSourceUnavailable,
                    "terrainLodCoarseBuilding" => state.CoarseCoverBuilding,
                    "terrainLodCoarseTransportPending" => state.CoarseCoverTransportPending,
                    "terrainLodCoarseGpuPending" => state.CoarseCoverGpuPending,
                    "terrainLodCoarseReady" => state.CoarseCoverReady,
                    "terrainLodCoarseAwaitingRequest" => state.CoarseCoverAwaitingRequest,
                    "terrainLodCoarseFrontierUnknown" => state.CoarseCoverFrontierUnknown,
                    "terrainLodCoarseComplete" => state.CoarseCoverComplete ? 1 : 0,
                    "terrainLodCoarseRetainingPrevious" =>
                        state.CoarseCoverRetainingPrevious ? 1 : 0,
                    "terrainLodColdCoverMs" => state.ColdCoverMs,
                    "terrainLodFirstCompleteHorizonMs" => state.FirstCompleteHorizonMs,
                    "terrainLodRefinementMs" => state.RefinementMs,
                    "terrainLodConvergenceGeneration" => state.Convergence.Generation,
                    "terrainLodFirstRequestMs" => state.Convergence.FirstRequestMs,
                    "terrainLodFirstSourceTileMs" => state.Convergence.FirstSourceTileMs,
                    "terrainLodSourceCompleteMs" => state.Convergence.SourceCompleteMs,
                    "terrainLodFirstBodyUploadMs" => state.Convergence.FirstBodyUploadMs,
                    "terrainLodBodiesCompleteMs" => state.Convergence.BodiesCompleteMs,
                    "terrainLodFirstSeamUploadMs" => state.Convergence.FirstSeamUploadMs,
                    "terrainLodSeamsCompleteMs" => state.Convergence.SeamsCompleteMs,
                    "terrainLodPublicationMs" => state.Convergence.PublicationMs,
                    "terrainLodBodyUploads" => state.Convergence.BodyUploads,
                    "terrainLodBodyUploadBytes" => state.Convergence.BodyUploadBytes,
                    "terrainLodBodyInstallMs" => state.Convergence.BodyInstallMs,
                    "terrainLodSeamUploads" => state.Convergence.SeamUploads,
                    "terrainLodSeamUploadBytes" => state.Convergence.SeamUploadBytes,
                    "terrainLodSeamInstallMs" => state.Convergence.SeamInstallMs,
                    "terrainLodIdentityReady" =>
                        terrainNetwork?.TerrainLodIdentityReady == true ? 1 : 0,
                    "terrainLodIdentityMismatches" =>
                        terrainNetwork?.TerrainLodIdentityMismatches ?? 0,
                    "terrainLodIdentityRejectedMessages" =>
                        terrainNetwork?.TerrainLodIdentityRejectedMessages ?? 0,
                    "clientWorkingSetBytes" => Environment.WorkingSet,
                    _ => 0
                };
            };
            LuauClientStateHost.EntityLodMetric = key =>
            {
                var lod = WorldRenderer?.EntityLod.Last ?? default;
                return key switch
                {
                    "entityClientResident" => WorldRenderer?.CountEntitiesTotal ?? 0,
                    "entityPresented" => WorldRenderer?.CountEntitiesRendered ?? 0,
                    "entityHidden" => WorldRenderer?.CountEntitiesHidden ?? 0,
                    "entityLodObserved" => lod.Observed,
                    "entityLodIntendedImpostors" => lod.IntendedImpostors,
                    "entityLodModelSubmissions" => lod.ModelDraws,
                    "entityLodImpostorSubmissions" => lod.ImpostorDraws,
                    "entityLodUnsupportedProvider" => lod.UnsupportedProvider,
                    "entityLodUnsupportedState" => lod.UnsupportedState,
                    "entityLodInvalidView" => lod.InvalidView,
                    "entityLodCapacityFallbacks" => lod.CapacityFallbacks,
                    "entityLodTransitions" => lod.TierTransitions,
                    "entityLodStateCount" => lod.RetainedStates,
                    "entityLodResets" => lod.Resets,
                    "entityImpostorViews" => WorldRenderer?.EntityImpostors.CompletedViews ?? 0,
                    "entityImpostorReady" => WorldRenderer?.EntityImpostors.Ready == true ? 1 : 0,
                    "entityImpostorFailures" => WorldRenderer?.EntityImpostors.Failures ?? 0,
                    "entityImpostorReplacements" => WorldRenderer?.EntityImpostors.Replacements ?? 0,
                    "entityImpostorPendingFallbacks" => WorldRenderer?.EntityImpostors.PendingFallbacks ?? 0,
                    "entityImpostorPoseMask" => WorldRenderer?.EntityImpostors.LastPoseMask ?? 0,
                    "entityImpostorHurtSubmissions" => WorldRenderer?.EntityImpostors.LastHurtSubmitted ?? 0,
                    "entityImpostorOverlaySubmissions" => WorldRenderer?.EntityImpostors.LastOverlaySubmitted ?? 0,
                    "entityImpostorMemoryHits" => WorldRenderer?.EntityImpostors.MemoryHits ?? 0,
                    "entityImpostorDiskHits" => WorldRenderer?.EntityImpostors.DiskHits ?? 0,
                    "entityImpostorCacheMisses" => WorldRenderer?.EntityImpostors.CacheMisses ?? 0,
                    "entityImpostorCacheWrites" => WorldRenderer?.EntityImpostors.CacheWrites ?? 0,
                    "entityImpostorCacheErrors" => WorldRenderer?.EntityImpostors.CacheErrors ?? 0,
                    "entityImpostorCancellations" => WorldRenderer?.EntityImpostors.Cancellations ?? 0,
                    "entityImpostorStaleResults" => WorldRenderer?.EntityImpostors.StaleResults ?? 0,
                    "entityImpostorCapturedViews" => WorldRenderer?.EntityImpostors.CapturedViews ?? 0,
                    "entityImpostorMemoryBytes" => WorldRenderer?.EntityImpostors.MemoryBytes ?? 0,
                    "entityImpostorReadbackPending" => WorldRenderer?.EntityImpostors.ReadbackPending == true ? 1 : 0,
                    "entityImpostorInvalidations" => WorldRenderer?.EntityImpostors.Invalidations ?? 0,
                    "entityImpostorBakeQueueAgeMs" => WorldRenderer?.EntityImpostors.OldestBakeQueueAgeMs ?? 0,
                    "entityImpostorLastBakeMs" => WorldRenderer?.EntityImpostors.LastBakeLatencyMs ?? 0,
                    "entityImpostorAverageBakeMs" => WorldRenderer?.EntityImpostors.AverageBakeLatencyMs ?? 0,
                    "entityImpostorCaptureCpuMs" => WorldRenderer?.EntityImpostors.CaptureCpuMs ?? 0,
                    "entityImpostorResidentGpuBytes" => WorldRenderer?.EntityImpostors.ResidentGpuBytes ?? 0,
                    "entityImpostorStagingBytes" => WorldRenderer?.EntityImpostors.StagingBytes ?? 0,
                    "entityImpostorDrawBatches" => WorldRenderer?.EntityImpostors.LastDrawBatches ?? 0,
                    "entityImpostorResidentAtlases" => WorldRenderer?.EntityImpostors.ResidentAtlasCount ?? 0,
                    "entityDistanceDespawnVisuals" => World is ClientWorld clientWorld
                        ? clientWorld.DistanceDespawnVisuals.Count : 0,
                    "entityDistanceDespawnPresentationCount" => World is ClientWorld despawnWorld
                        ? despawnWorld.DistanceDespawnPresentationCount : 0,
                    "webGpuErrorCount" => WebGpuDevice.Current?.ErrorCount ?? 0,
                    _ => 0
                };
            };
            LuauClientStateHost.OldestForegroundAge = () => WorldRenderer?.ChunkRenderer.OldestForegroundAge ?? 0;
            LuauClientStateHost.PresentationRegressionCount = () =>
                WorldRenderer?.ChunkRenderer.PresentationRegressionCount ?? 0;
            LuauClientStateHost.PlayerX = () => Player?.X ?? 0;
            LuauClientStateHost.PlayerY = () => Player?.Y ?? 0;
            LuauClientStateHost.PlayerZ = () => Player?.Z ?? 0;
            LuauClientStateHost.Install(LuauState.Handle);
            if (!LuauState.TryExecute(LuauClientStateHost.Bootstrap, out var clientStateBootstrapError))
            {
                _logger.LogError("Failed to install the Luau client-state bootstrap: {Error}", clientStateBootstrapError);
            }

            if (_e2eTestController != null)
            {
                LuauTestHost.Pass = _e2eTestController.Pass;
                LuauTestHost.Fail = reason => _e2eTestController.Fail(reason);
                LuauTestHost.Creative = () => Player?.SendChatMessage("/gm c");
                LuauTestHost.Summon = (entity, count) =>
                {
                    if (Player == null || count is < 1 or > 256 || !ResourceLocation.TryParse(entity, out _))
                        return false;
                    Player.SendChatMessage($"/summon {entity} {count}");
                    return true;
                };
                LuauTestHost.CountEntities = (entity, minDistance, maxDistance) =>
                {
                    if (World == null || Player == null || minDistance < 0 || maxDistance < minDistance ||
                        !ResourceLocation.TryParse(entity, out var key) ||
                        !World.Content.EntityTypes.TryGet(key!, out var type)) return 0;
                    var minSquared = minDistance * minDistance;
                    var maxSquared = maxDistance * maxDistance;
                    return World.Entities.Entities.Count(candidate =>
                    {
                        if (!ReferenceEquals(candidate.Type, type)) return false;
                        var squared = candidate.GetSquaredDistance(Player.X, Player.Y, Player.Z);
                        return squared >= minSquared && squared <= maxSquared;
                    });
                };
                LuauTestHost.BreakBlock = (x, y, z) =>
                {
                    if (PlayerController == null || World == null ||
                        World.Reader.GetBlockId(x, y, z) == 0)
                        return false;
                    PlayerController.ClickBlock(x, y, z, 1);
                    return true;
                };
                LuauTestHost.SetBlock = (id, x, y, z) =>
                {
                    if (string.IsNullOrWhiteSpace(id)) return;
                    Player?.SendChatMessage($"/block set {id} {x} {y} {z}");
                };
                LuauTestHost.IsMeshCurrent = (x, y, z) =>
                    WorldRenderer?.ChunkRenderer.IsMeshCurrent(x, y, z) == true;
                LuauTestHost.HasBlock = (id, x, y, z) =>
                    World != null && y >= 0 && y < ChuckFormat.WorldHeight &&
                    World.BlockHost.HasChunk(x >> 4, z >> 4) &&
                    World.Reader.GetBlockId(x, y, z) ==
                    (id == "omniblock:air" ? 0 : World.Content.Blocks.Get(id).Id);
                LuauTestHost.MeshDeadlineMissCount = (x, y, z) =>
                    WorldRenderer?.ChunkRenderer.CriticalDeadlineMissesAt(x, y, z) ?? 0;
                LuauTestHost.SetFlying = flying => Player?.SetFlyingForTest(flying);
                LuauTestHost.Teleport = (x, y, z) => Player?.SendChatMessage($"/tp {x} {y} {z}");
                LuauTestHost.SetLook = (yaw, pitch) =>
                {
                    if (Player == null) return;
                    Player.PrevYaw = Player.Yaw = (float)yaw;
                    Player.PrevPitch = Player.Pitch = Math.Clamp((float)pitch, -90.0F, 90.0F);
                };
                LuauTestHost.SetMovement = (forward, strafe, vertical) =>
                    Player?.SetMovementForTest((float)forward, (float)strafe, (float)vertical);
                LuauTestHost.FlyPath = (ax, ay, az, bx, by, bz, seconds) =>
                    Player?.StartFlightPathForTest(ax, ay, az, bx, by, bz, seconds);
                LuauTestHost.Screenshot = () => WebGpuRenderer.ScreenshotRequested = true;
                var impostorTestControls = new EntityImpostorTestControls(this);
                LuauTestHost.ImpostorCache = impostorTestControls.Apply;
                LuauTestHost.EntityImpostors = (enabled, forceForTest) =>
                {
                    if (WorldRenderer == null) return false;
                    WorldRenderer.EntityImpostors.Enabled = enabled;
                    WorldRenderer.EntityImpostors.ForceTierForTest = forceForTest;
                    if (!enabled) WorldRenderer.EntityImpostors.Reset();
                    return true;
                };
                LuauTestHost.EntityBaseline = (scene, count, distance) =>
                {
                    EntityBaseline?.Dispose();
                    EntityBaseline = null;
                    EntityBaseline = new EntityRenderBaseline(this, scene, count, distance);
                    return true;
                };
                LuauTestHost.EntityBaselineState = state => EntityBaseline?.SetImpostorState(state) == true;
                LuauTestHost.EntityBaselineEnvironment = state => EntityBaseline?.SetEnvironment(state) == true;
                LuauTestHost.BeginEntitySample = () =>
                {
                    if (EntityBaseline == null) return false;
                    EntityBaseline.BeginSample();
                    return true;
                };
                LuauTestHost.EndEntitySample = label =>
                {
                    if (EntityBaseline == null) return false;
                    var result = EntityBaseline.FinishSample();
                    _e2eTestController.WriteTextArtifact($"entity-baseline-{label}.json", result.Json);
                    return result.Valid;
                };
                LuauTestHost.ClearEntityBaseline = () =>
                {
                    EntityBaseline?.Dispose();
                    EntityBaseline = null;
                };
                LuauTestHost.DumpTerrain = label =>
                {
                    if (Player == null || WorldRenderer?.ChunkRenderer == null) return;
                    _e2eTestController.WriteTextArtifact(
                        $"terrain-{label}.tsv",
                        WorldRenderer.ChunkRenderer.CreateTerrainStateDump(
                            new Vector3D<double>(Player.X, Player.Y, Player.Z)));
                    _e2eTestController.WriteTextArtifact(
                        $"mesh-lifecycle-{label}.tsv", WorldRenderer.ChunkRenderer.CreateMeshLifecycleDump());
                    _e2eTestController.WriteTextArtifact(
                        $"mesh-sections-{label}.tsv", WorldRenderer.ChunkRenderer.CreateMeshSectionDump());
                    if (WorldRenderer.TerrainLod is { } terrainLod)
                        _e2eTestController.WriteTextArtifact(
                            $"terrain-lod-{label}.json",
                            JsonSerializer.Serialize(new
                            {
                                Terrain = terrainLod.Snapshot,
                                Spatial = terrainLod.SpatialSnapshot,
                                Coverage = terrainLod.CoverageSnapshot,
                                Server = InternalServer?.worlds?
                                    .FirstOrDefault(world => world.Dimension.Id == World.Dimension.Id)?
                                    .TerrainLodSnapshot
                            }, new JsonSerializerOptions { WriteIndented = true }));
                    var worldGeneration = InternalServer?.worlds?
                        .FirstOrDefault(world => world.Dimension.Id == World.Dimension.Id)?
                        .ChunkCache.GenerationTelemetry.Snapshot()
                        ?? InternalServer?.worlds?.FirstOrDefault()?.ChunkCache.GenerationTelemetry.Snapshot();
                    if (worldGeneration is not null)
                        _e2eTestController.WriteTextArtifact(
                            $"world-generation-{label}.json",
                            JsonSerializer.Serialize(worldGeneration, new JsonSerializerOptions
                            {
                                WriteIndented = true
                            }));
                };
                LuauTestHost.DumpProfiler = label =>
                {
                    _e2eTestController.WriteTextArtifact(
                        $"profiler-{label}.tsv", Profiler.CreateTsvSnapshot());
                    if (WebGpuDevice.Current?.GpuProfiler is { } gpuProfiler)
                        _e2eTestController.WriteTextArtifact(
                            $"gpu-profiler-{label}.tsv", gpuProfiler.CreateTsvSnapshot());
                    if (WorldRenderer?.ChunkRenderer is { } chunkRenderer)
                        _e2eTestController.WriteTextArtifact(
                            $"chunk-presentation-{label}.tsv",
                            chunkRenderer.CreatePresentationProfileDump());
                };
                LuauTestHost.WorldGenerationAuto = (profile, radius) =>
                {
                    if (Player == null || InternalServer == null) return false;
                    var normalized = profile.ToLowerInvariant();
                    if (normalized == "off")
                    {
                        Player.SendChatMessage("/worldgen auto off");
                        return true;
                    }
                    if (normalized is not ("play" or "prepare") || radius is < 4 or > 256)
                        return false;
                    Player.SendChatMessage($"/worldgen auto on {radius} {normalized}");
                    return true;
                };
                LuauTestHost.WorldGenerationMetric = metric =>
                {
                    var dimension = Player?.DimensionId ?? 0;
                    var snapshot = InternalServer?.GetAutomaticPregenerationSnapshots()
                        .FirstOrDefault(candidate => candidate.Dimension == dimension);
                    if (snapshot is null) return 0;
                    return metric switch
                    {
                        "enabled" => snapshot.Options.Enabled ? 1 : 0,
                        "radius" => snapshot.Options.RadiusChunks,
                        "players" => snapshot.ActivePlayers,
                        "prepared" => snapshot.PreparedTargets,
                        "saved" => snapshot.SavedTargets,
                        "skipped" => snapshot.SkippedTargets,
                        "gameplayDeferred" => snapshot.GameplayDeferredTargets,
                        "written" => snapshot.WrittenChunks,
                        "active" => snapshot.ActiveTarget is null ? 0 : 1,
                        "throttled" => snapshot.Throttled ? 1 : 0,
                        "gameplayPending" => snapshot.Pressure.GameplayPending,
                        "backgroundQueued" => snapshot.Pressure.BackgroundQueued,
                        "lightingPending" => snapshot.Pressure.LightingPending,
                        "serverTickMs" => snapshot.Pressure.ServerTickMs,
                        "clientFrameMs" => snapshot.Pressure.IntegratedClientFrameMs ?? 0,
                        "memoryLoadRatio" => snapshot.Pressure.MemoryLoadRatio,
                        "diskFreeBytes" => snapshot.Pressure.DiskFreeBytes,
                        "peakRetainedBytes" => snapshot.PeakRetainedBytes,
                        _ => 0
                    };
                };
                LuauTestHost.ConfigureTerrainLodScaleProfile = horizonChunks =>
                {
                    // This capability exists only in an explicit E2E launch. Keep it immutable for
                    // the lifetime of a world: changing hierarchy depth after identity negotiation
                    // would make the client, integrated server, and durable cache disagree.
                    if (World != null || InternalServer != null ||
                        horizonChunks is not (512 or 1024))
                        return false;
                    _terrainLodTestHorizonChunks = horizonChunks;
                    _logger.LogInformation(
                        "Configured E2E-only terrain LOD scale profile: {Horizon} chunks, L{Level}",
                        horizonChunks,
                        TerrainLodSpatialPolicy.RequiredMaximumSpatialLevel(horizonChunks));
                    return true;
                };
                LuauTestHost.Disconnect = () =>
                {
                    if (World == null || _testDisconnectRequested) return false;
                    // Apply after the scheduler returns, so disposal cannot reenter the VM.
                    _testDisconnectRequested = true;
                    return true;
                };
                LuauTestHost.PrepareTerrainLodFixture = (radius, x, z) =>
                {
                    if (Player == null || InternalServer == null ||
                        radius <= 0 ||
                        radius > (_terrainLodTestHorizonChunks ?? 64) ||
                        x.HasValue != z.HasValue ||
                        (x.HasValue && (!double.IsFinite(x.Value) || !double.IsFinite(z!.Value) ||
                            Math.Abs(x.Value) > 30_000_000 || Math.Abs(z.Value) > 30_000_000)))
                        return false;
                    return InternalServer.QueueTerrainLodScaleFixture(
                        Player.DimensionId,
                        (x ?? Player.X) / 16.0,
                        (z ?? Player.Z) / 16.0,
                        Options.RenderDistance,
                        radius);
                };
                LuauTestHost.TerrainLodFixtureMetric = metric =>
                {
                    var snapshot = InternalServer?.TerrainLodScaleFixture;
                    if (metric == "cacheReadHits")
                    {
                        var dimension = Player?.DimensionId ?? 0;
                        return InternalServer?.getWorld(dimension).TerrainLodSnapshot?
                            .SpatialCache.ReadHits ?? 0;
                    }
                    if (metric == "cacheEntries")
                    {
                        var dimension = Player?.DimensionId ?? 0;
                        return InternalServer?.getWorld(dimension).TerrainLodSnapshot?
                            .SpatialCache.EntryCount ?? 0;
                    }
                    if (metric is "cacheBytes" or "readQueued" or "readPending" or
                        "encodeQueued" or "encodePending" or "wirePayloads" or
                        "preparationPending" or "preparationFailures" or "offlineSubmitted" or
                        "offlineDropped" or "persistenceDeferred" or "persistenceRunning")
                    {
                        var dimension = Player?.DimensionId ?? 0;
                        var terrain = InternalServer?.getWorld(dimension).TerrainLodSnapshot;
                        return metric switch
                        {
                            "cacheBytes" => terrain?.SpatialCache.CurrentBytes ?? 0,
                            "readQueued" => terrain?.SpatialReadQueued ?? 0,
                            "readPending" => terrain?.SpatialReadPending ?? 0,
                            "encodeQueued" => terrain?.SpatialEncodeQueued ?? 0,
                            "encodePending" => terrain?.SpatialEncodePending ?? 0,
                            "wirePayloads" => terrain?.SpatialWirePayloads ?? 0,
                            "preparationPending" => terrain?.PreparationPendingWork ?? -1,
                            "preparationFailures" => terrain?.PreparationFailureEvents ?? -1,
                            "offlineSubmitted" => terrain?.OfflineSnapshotsSubmitted ?? -1,
                            "offlineDropped" => terrain?.OfflineSnapshotsDropped ?? -1,
                            "persistenceDeferred" => terrain?.SpatialHierarchy.PersistenceDeferrals ?? -1,
                            "persistenceRunning" => terrain?.SpatialHierarchy.Persistence?.Running ?? -1,
                            _ => 0
                        };
                    }
                    if (snapshot is null) return 0;
                    return metric switch
                    {
                        "queued" => snapshot.Status == TerrainLodScaleFixtureStatus.Queued ? 1 : 0,
                        "preparing" => snapshot.Status == TerrainLodScaleFixtureStatus.Preparing ? 1 : 0,
                        "complete" => snapshot.Status == TerrainLodScaleFixtureStatus.Complete ? 1 : 0,
                        "failed" => snapshot.Status == TerrainLodScaleFixtureStatus.Failed ? 1 : 0,
                        "tiles" => snapshot.Tiles,
                        _ => 0
                    };
                };
                LuauTestHost.Install(LuauState.Handle);
                if (!LuauState.TryExecute(LuauTestHost.Bootstrap, out var testBootstrapError))
                {
                    _logger.LogError("Failed to install the Luau E2E-test bootstrap: {Error}", testBootstrapError);
                    _e2eTestController.Fail($"Failed to install the E2E-test API: {testBootstrapError}");
                }
            }

            _luauWorldService = new LuauWorldService(
                SaveLoader,
                () => World == null && InternalServer == null);
            LuauWorldsHost.List = _luauWorldService.List;
            LuauWorldsHost.Load = _luauWorldService.RequestLoad;
            LuauWorldsHost.Install(LuauState.Handle);
            if (!LuauState.TryExecute(LuauWorldsHost.Bootstrap, out var worldsBootstrapError))
            {
                _logger.LogError("Failed to install the Luau worlds bootstrap: {Error}", worldsBootstrapError);
            }

            LuauWorldGenerationHost.Available = () => InternalServer != null;
            LuauWorldGenerationHost.List = GetLuauWorldGenerationSnapshots;
            LuauWorldGenerationHost.Inspect = id => GetLuauWorldGenerationSnapshots()
                .FirstOrDefault(snapshot => snapshot.Id == id) is { Id.Length: > 0 } snapshot
                    ? snapshot
                    : null;
            LuauWorldGenerationHost.Start = QueueLuauWorldGenerationStart;
            LuauWorldGenerationHost.Change = QueueLuauWorldGenerationChange;
            LuauWorldGenerationHost.Install(LuauState.Handle);
            if (!LuauState.TryExecute(LuauWorldGenerationHost.Bootstrap, out var generationBootstrapError))
            {
                _logger.LogError(
                    "Failed to install the Luau world-generation bootstrap: {Error}",
                    generationBootstrapError);
            }

            LuauUiHost.Dispatch = UiCommandRegistry.Invoke;
            LuauUiHost.Install(LuauState.Handle);

            LuauRegistryHost.RegisterUi = (name, out id) =>
            {
                // Every exception this could throw — an invalid ResourceLocation from a
                // malformed name, or UiCommandRegistry's own frozen check — must not propagate
                // into RegisterUiClosure's [UnmanagedCallersOnly] body
                // (docs/luau-registry-phase-plan.md §4). This lambda is the one place that
                // actually calls into UiCommandRegistry, so it's the only place that can catch
                // what that call might throw; a bare catch is deliberate here, not laziness —
                // the boundary this guards doesn't care which exception type crossed it, only
                // that none does.
                try
                {
                    id = UiCommandRegistry.ResolveOrCreate(name);
                    return true;
                }
                catch (Exception)
                {
                    id = -1;
                    return false;
                }
            };
            LuauRegistryHost.Install(LuauState.Handle);
        }

        // Every mod's Registry-phase module must run — and every Registry.registerUi call it
        // makes must land — strictly before this line; nothing calls Register/ResolveOrCreate
        // anywhere yet (no mod loader exists to call it from — CLAUDE.md's scripting layer "has
        // not landed yet"), so this ordering is currently unobserved rather than exercised. Once
        // mod content registration is a real phase of boot, whether it runs before or after this
        // point is docs/luau-ui-host-api-plan.md Open Question #2, still open; revisit this
        // freeze's position then rather than assuming today's placement is final.
        UiCommandRegistry.Freeze();

        TexturePackList = new TexturePacks(this, new DirectoryInfo(GameDataDir));
        TextureManager = new TextureManager(this, TexturePackList, Options,
            EntityRenderDispatcher.Instance.ImpostorTextureDependencies);
        TextRenderer = new TextRenderer(Options, TextureManager);

        var terrainTexture = TextureManager.GetTextureId("/terrain.png");
        var itemsTexture = TextureManager.GetTextureId("/gui/items.png");

        BuildBatchRenderer(terrainTexture.Id, itemsTexture.Id);

        UIContext = new UIContext(
            Options,
            TextRenderer,
            UiBatchRenderer,
            TextureManager,
            terrainTexture,
            itemsTexture,
            () => SoundManager.PlaySoundFX("random.click", 1.0f, 1.0f),
            () => new Vector2D<int>(DisplayWidth, DisplayHeight),
            () =>
            {
                if (!Options.ShowDebugInfo || _debugWindowManager == null)
                {
                    return new Vector2D<int>(DisplayWidth, DisplayHeight);
                }

                var vs = _debugWindowManager.ViewportSize;
                return vs is { X: > 0, Y: > 0 } ? new Vector2D<int>((int)vs.X, (int)vs.Y) : new Vector2D<int>(DisplayWidth, DisplayHeight);
            },
            this,
            VirtualCursor,
            Timer,
            this,
            () => World != null,
            () => World,
            () => new Vector2D<int>((int)DebugViewportOffset.X, (int)DebugViewportOffset.Y),
            () =>
            {
                if (WebGpuRenderer.FramebufferSize is { Width: > 0, Height: > 0 } size)
                {
                    return new Vector2D<int>((int)size.Width, (int)size.Height);
                }

                return new Vector2D<int>(Display.getFramebufferWidth(), Display.getFramebufferHeight());
            },
            Content
        );

        SkinManager = new SkinManager(TextureManager);
        WaterColors.loadColors(TextureManager.GetColors("/misc/watercolor.png"));
        GrassColors.loadColors(TextureManager.GetColors("/misc/grasscolor.png"));
        FoliageColors.loadColors(TextureManager.GetColors("/misc/foliagecolor.png"));
        GameRenderer = new GameRenderer(this);
        EntityRenderDispatcher.Instance.SkinManager = SkinManager;
        EntityRenderDispatcher.Instance.HeldItemRenderer = new HeldItemRenderer(this);
        StatFileWriter = new StatFileWriter(Session, GameDataDir);
        /*global::OmniBlock.Achievements.OpenInventory.GetTranslatedDescription = () =>
        {
            return format.formatString(global::OmniBlock.Achievements.OpenInventory.TranslationKey);
        };*/
    }

    private void BuildBatchRenderer(int terrainTextureId, int itemsTextureId)
    {
        UiBatchRenderer = new UIBatchRenderer(Options);

        UiBatchRenderer.RegisterTextureByPath("terrain.png", (uint)terrainTextureId);
        UiBatchRenderer.RegisterTextureByPath("gui/items.png", (uint)itemsTextureId);

        var fontTexId = TextRenderer.FontTextureId;
        if (fontTexId != 0)
            UiBatchRenderer.RegisterTextureByPath("font/default.png", fontTexId);

        RegisterCommonTexture("gui/gui.png");
        RegisterCommonTexture("gui/icons.png");
        RegisterCommonTexture("gui/background.png");
        RegisterCommonTexture("gui/inventory.png");
        RegisterCommonTexture("gui/container.png");
        RegisterCommonTexture("gui/crafting.png");
        RegisterCommonTexture("gui/trap.png");
        RegisterCommonTexture("gui/furnace.png");
    }

    private void RegisterCommonTexture(string assetPath)
    {
        var handle = TextureManager.GetTextureId("/" + assetPath);
        UiBatchRenderer.RegisterTextureByPath(assetPath, (uint)handle.Id);
    }

    private unsafe void SetupRenderingAndInput()
    {
        // Anisotropy was a GL extension query; WebGPU has no ceiling to ask for, so this just
        // states the sampler field's fixed value.
        GameOptions.MaxAnisotropy = 1.0f;

        ImGui.CreateContext();

        // ImGuiImplGLFW is compiled into its own native DLL, with its own GImGui context pointer.
        // We must share the context created by cimgui.dll with it before calling its Init function.
        ImGuiImplGLFW.SetCurrentContext(ImGui.GetCurrentContext());

        ImGuiIO* io = ImGui.GetIO();
        io->ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.DockingEnable;
        // ImGui defaults to ./imgui.ini, which makes layout state depend on the launch directory
        // and lets source-tree tools accidentally rewrite it. Keep it with the rest of this
        // profile instead; disposable E2E data roots then isolate debug UI state as promised.
        _imguiIniFilename = Marshal.StringToCoTaskMemUTF8(Path.Combine(GameDataDir, "imgui.ini"));
        io->IniFilename = (byte*)_imguiIniFilename;

        // Install game input callbacks first so the ImGui GLFW backend can chain to them.
        Keyboard.create(Display.getGlfw(), Display.GetWindowHandle());
        Mouse.create(Display.getGlfw(), Display.GetWindowHandle(), Display.getWidth(), Display.getHeight());
        Controller.Create(Display.getGlfw(), Display.GetWindowHandle());

        // Despite the name, InitForOpenGL sets up only ImGui's GLFW input backend — the renderer
        // half is ImGuiWgpuBackend.
        ImGuiImplGLFW.InitForOpenGL((GLFWwindow*)Display.GetWindowHandle(), true);
        DebugWindowManager.ApplyStyle();

        _debugWindowManager = new DebugWindowManager(this, () => InGameHasFocus);

        ControllerManager.Initialize(this);
        MouseHelper = new MouseHelper
        {
            GetUngrabCenter = () =>
            {
                if (!Options.ShowDebugInfo || _debugWindowManager == null)
                {
                    return new Vector2D<int>(Display.getWidth() / 2, Display.getHeight() / 2);
                }

                var vp = _debugWindowManager.ViewportPos;
                var vs = _debugWindowManager.ViewportSize;
                return vs is { X: > 0, Y: > 0 } ? new Vector2D<int>((int)(vp.X + vs.X / 2), (int)(vp.Y + vs.Y / 2)) : new Vector2D<int>(Display.getWidth() / 2, Display.getHeight() / 2);
            }
        };

        RenderSystem.TextureEnabled = true;
        RenderSystem.ShadeModel = ShadeModel.Smooth;

        // The state every frame starts from, and the one the rest of the renderer is traced
        // against. It is named here rather than assembled from a handful of enables so that the
        // applier's cache starts out true instead of empty: depth tested and written, compared
        // Lequal, nothing blended, nothing culled. Culling being off is not an oversight — GL
        // starts with it disabled and the old code only ever set which face to cull, never turned
        // it on, which is the same thing RenderState.Entity settles on for the entity pass.
        RenderSystem.State.Apply(RenderState.Entity);

        RenderSystem.AlphaTestEnabled = true;
        RenderSystem.AlphaThreshold = 0.1F;
        // Both stacks to identity. The model-view holds the default from process start, but
        // stating it explicitly means a later stack-owner change doesn't silently infect this.
        RenderSystem.Projection.LoadIdentity();
        RenderSystem.ModelView.LoadIdentity();
    }

    private void SetupResourcesAndPostProcessing()
    {
        RegistryAccess = RegistryAccess.Build();

        SoundManager.LoadSoundSettings(Options);
        DefaultMusicCategories.Register(SoundManager);

        TextureManager.AddDynamicTexture(_textureLavaFX);
        TextureManager.AddDynamicTexture(_textureWaterFX);
        TextureManager.AddDynamicTexture(new NetherPortalSprite(Content.Blocks));
        TextureManager.AddDynamicTexture(new CompassSprite(this));
        TextureManager.AddDynamicTexture(new ClockSprite(this));
        TextureManager.AddDynamicTexture(new WaterSideSprite());
        TextureManager.AddDynamicTexture(new LavaSideSprite());
        TextureManager.AddDynamicTexture(new FireSprite("fire_layer_0", "custom_fire_e_w.png"));
        TextureManager.AddDynamicTexture(new FireSprite("fire_layer_1", "custom_fire_n_s.png"));

        WorldRenderer = new WorldRenderer(this, TextureManager);
        ApplyEntityImpostorOption(Options.EntityImpostors);
        ParticleManager = new ParticleManager(World, TextureManager, Options);

        _ = new ResourceManager()
            .Add(new BetaResourceDownloader(this, GameDataDir))
            .Add(new ModernAssetDownloader(this, GameDataDir,
            [
                "minecraft/sounds/music/menu/moog_city_2.ogg",
                "minecraft/sounds/music/menu/mutation.ogg",
                "minecraft/sounds/music/menu/floating_trees.ogg",
                "minecraft/sounds/music/menu/beginning_2.ogg"
            ])).LoadAllAsync();

        HUD = new HUD(UIContext, new HUDContext(
            () => Player,
            () => PlayerController,
            () => World,
            () => CurrentScreen == null && Player != null && World != null
                ? new InGameTipContext(ObjectMouseOver, World.Reader, Content.Blocks, Player.Inventory.ItemInHand)
                : null,
            () => _isMainMenuOpen
        ));

        EntityRenderDispatcher.Instance.SkinManager.RequestDownload(Session.username);
    }

    internal void ApplyEntityImpostorOption(bool enabled)
    {
        if (WorldRenderer == null) return;
        WorldRenderer.EntityImpostors.Enabled = enabled;
        WorldRenderer.EntityImpostors.ForceTierForTest = false;
        if (!enabled) WorldRenderer.EntityImpostors.Reset();
    }

    private void LoadVersion()
    {
        try
        {
            Version = File.Exists("version.txt") ? File.ReadAllText("version.txt").Trim().ToLower() : "development build";
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to load version: {}", ex.Message);
            Version = UnknownVersion;
        }
    }

    private void Shutdown() => Running = false;

    private void ShutdownGame()
    {
        // E2E completion has already persisted its authoritative result and captured log.
        // Avoid production teardown work that may wait on background network/resource tasks;
        // each scenario runs in its own process and disposable data directory.
        if (_e2eTestController is { IsCompleted: true } completedTest)
        {
            _logger.LogInformation("Stopping completed E2E client");
            Environment.Exit(completedTest.ExitCode);
        }

        try
        {
            StopInternalServer();
            StatFileWriter.Tick();
            StatFileWriter.SyncStats();

            _logger.LogInformation("Stopping!");

            try
            {
                ChangeWorld(null);
            }
            catch (Exception)
            {
            }

            // don't bother trying to shutdown imgui because it keeps hanging/crashing

            WorldRenderer?.Dispose();
            UiBatchRenderer?.Dispose();
            SkinManager.Dispose();
            TextureManager.Dispose();
            SoundManager.Dispose();
            LuauUiHost.Dispatch = null;
            LuauRegistryHost.RegisterUi = null;
            LuauDomHost.Query = null;
            LuauDomHost.Parent = null;
            LuauDomHost.ChildCount = null;
            LuauDomHost.Child = null;
            LuauDomHost.GetString = null;
            LuauDomHost.SetString = null;
            LuauDomHost.GetBool = null;
            LuauDomHost.SetBool = null;
            LuauDomHost.Click = null;
            LuauDomHost.Screen = null;
            LuauConfigHost.Get = null;
            LuauConfigHost.Set = null;
            LuauConfigHost.Options = null;
            LuauWorldsHost.List = null;
            LuauWorldsHost.Load = null;
            LuauWorldGenerationHost.Available = null;
            LuauWorldGenerationHost.List = null;
            LuauWorldGenerationHost.Inspect = null;
            LuauWorldGenerationHost.Start = null;
            LuauWorldGenerationHost.Change = null;
            LuauClientStateHost.WorldLoaded = null;
            LuauClientStateHost.PlayerReady = null;
            LuauClientStateHost.WorldId = null;
            LuauClientStateHost.DebugOpen = null;
            LuauClientStateHost.MeshPending = null;
            LuauClientStateHost.MeshCancelledCount = null;
            LuauClientStateHost.MeshSupersededCount = null;
            LuauClientStateHost.MeshBuildFailureCount = null;
            LuauClientStateHost.MeshAwaitingUpload = null;
            LuauClientStateHost.MeshAwaitingDraw = null;
            LuauClientStateHost.MeshLeadingEdgeQueued = null;
            LuauClientStateHost.MeshLeadingEdgePending = null;
            LuauClientStateHost.MeshEvictionGraceCount = null;
            LuauClientStateHost.MeshCooperativeCancellationCount = null;
            LuauClientStateHost.MeshCriticalCompletedCount = null;
            LuauClientStateHost.MeshCriticalDeadlineMissCount = null;
            LuauClientStateHost.MeshCriticalOverdueCount = null;
            LuauClientStateHost.MeshRequestToGpuMs = null;
            LuauClientStateHost.FrameTimeMs = null;
            LuauClientStateHost.MeshSafetyLoadedColumns = null;
            LuauClientStateHost.MeshSafetyExpectedSections = null;
            LuauClientStateHost.MeshSafetyHoles = null;
            LuauClientStateHost.MeshReadyRadius = null;
            LuauClientStateHost.ResidentMeshCount = null;
            LuauClientStateHost.PresentedMeshCount = null;
            LuauClientStateHost.ForegroundPending = null;
            LuauClientStateHost.BackgroundPending = null;
            LuauClientStateHost.LightRefreshPending = null;
            LuauClientStateHost.LightRefreshCompletedCount = null;
            LuauClientStateHost.GeometryUploadsLastFrame = null;
            LuauClientStateHost.LightUploadsLastFrame = null;
            LuauClientStateHost.SolidDrawsLastFrame = null;
            LuauClientStateHost.TranslucentDrawsLastFrame = null;
            LuauClientStateHost.ResidentSolidLayerCount = null;
            LuauClientStateHost.ResidentTranslucentLayerCount = null;
            LuauClientStateHost.VisibilityCandidates = null;
            LuauClientStateHost.VisibilityReuseFrames = null;
            LuauClientStateHost.VisibilitySynchronousFrames = null;
            LuauClientStateHost.VisibilityBuilds = null;
            LuauClientStateHost.VisibilityBuildCancellations = null;
            LuauClientStateHost.VisibilityStaleResults = null;
            LuauClientStateHost.VisibilityBuildInFlight = null;
            LuauClientStateHost.VisibilityWorkerCandidates = null;
            LuauClientStateHost.FrustumTests = null;
            LuauClientStateHost.PortalVisited = null;
            LuauClientStateHost.SafetyRescued = null;
            LuauClientStateHost.RenderDistance = null;
            LuauClientStateHost.SimulationDistance = null;
            LuauClientStateHost.PresentedSolidLayerCount = null;
            LuauClientStateHost.PresentedTranslucentLayerCount = null;
            LuauClientStateHost.EmptyLayersSubmitted = null;
            LuauClientStateHost.TerrainDrawCalls = null;
            LuauClientStateHost.TerrainUniformEntries = null;
            LuauClientStateHost.TerrainSubmissionBatches = null;
            LuauClientStateHost.TerrainPipelineBinds = null;
            LuauClientStateHost.TerrainTextureBinds = null;
            LuauClientStateHost.TerrainUniformArenaCapacity = null;
            LuauClientStateHost.TerrainUniformArenaGrowths = null;
            LuauClientStateHost.FindVisibleMs = null;
            LuauClientStateHost.TerrainSubmitCpuMs = null;
            LuauClientStateHost.TerrainLodMetric = null;
            LuauClientStateHost.EntityLodMetric = null;
            LuauClientStateHost.OldestForegroundAge = null;
            LuauClientStateHost.PresentationRegressionCount = null;
            LuauClientStateHost.PlayerX = null;
            LuauClientStateHost.PlayerY = null;
            LuauClientStateHost.PlayerZ = null;
            LuauTestHost.Pass = null;
            LuauTestHost.Fail = null;
            LuauTestHost.Creative = null;
            LuauTestHost.Disconnect = null;
            LuauTestHost.Summon = null;
            LuauTestHost.CountEntities = null;
            LuauTestHost.BreakBlock = null;
            LuauTestHost.SetBlock = null;
            LuauTestHost.HasBlock = null;
            LuauTestHost.IsMeshCurrent = null;
            LuauTestHost.MeshDeadlineMissCount = null;
            LuauTestHost.SetFlying = null;
            LuauTestHost.Teleport = null;
            LuauTestHost.SetLook = null;
            LuauTestHost.SetMovement = null;
            LuauTestHost.FlyPath = null;
            LuauTestHost.Screenshot = null;
            LuauTestHost.DumpTerrain = null;
            LuauTestHost.DumpProfiler = null;
            LuauTestHost.WorldGenerationAuto = null;
            LuauTestHost.WorldGenerationMetric = null;
            LuauTestHost.ConfigureTerrainLodScaleProfile = null;
            LuauTestHost.PrepareTerrainLodFixture = null;
            LuauTestHost.TerrainLodFixtureMetric = null;
            LuauTestHost.EntityBaseline = null;
            LuauTestHost.EntityBaselineState = null;
            LuauTestHost.EntityBaselineEnvironment = null;
            LuauTestHost.BeginEntitySample = null;
            LuauTestHost.EndEntitySample = null;
            LuauTestHost.ClearEntityBaseline = null;
            LuauTestHost.EntityImpostors = null;
            LuauTestHost.ImpostorCache = null;
            EntityBaseline?.Dispose();
            EntityBaseline = null;
            _luauWorldService = null;
            LuauLogHost.WriteLine = null;
            LuauState?.Dispose();
            Mouse.destroy();
            Keyboard.destroy();

            Texture2D.LogLeakReport();
            if (_imguiIniFilename != 0)
            {
                Marshal.FreeCoTaskMem(_imguiIniFilename);
                _imguiIniFilename = 0;
            }
        }
        finally
        {
            Display.destroy();
            CleanupTimer();

            if (_e2eTestController != null)
            {
                _e2eTestController.EnsureCompleted();
                Environment.ExitCode = _e2eTestController.ExitCode;
                _e2eTestController.Dispose();
            }
            else if (!_hasCrashed)
            {
                Environment.Exit(0);
            }
        }
    }

    private void CrashCleanup()
    {
        try
        {
            ChangeWorld(null);
        }
        catch (Exception)
        {
            // ignored
        }
    }

    private void OnGameCrash(Exception crashInfo)
    {
        _hasCrashed = true;
        _logger.LogError(crashInfo, "OmniBlock has crashed!");
        _e2eTestController?.Fail($"Client crashed: {crashInfo.Message}");
    }

    #endregion

    #region Main Game Loop

    private void Run()
    {
        Running = true;

        try
        {
            StartGame();
        }
        catch (Exception startupException)
        {
            OnGameCrash(startupException);
            return;
        }

        try
        {
            var lastFpsCheckTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var frameCounter = 0;

            while (Running)
            {
                var frameStartNano = Stopwatch.GetTimestamp();

                Profiler.Update(Timer.DeltaTime);

                try
                {
                    if (Display.isCloseRequested())
                    {
                        Shutdown();
                    }

                    Controller.PollEvents();
                    if (Controller.IsActive() && !IsControllerMode)
                    {
                        Mouse.setCursorVisible(false);
                        IsControllerMode = true;
                    }

                    if (IsControllerMode && CurrentScreen != null)
                    {
                        var inputSize = UIContext.InputDisplaySize;
                        VirtualCursor.Update(CurrentScreen, Options, inputSize.X, inputSize.Y, Timer.DeltaTime);
                    }

                    if (IsGamePaused && World != null)
                    {
                        var previousRenderPartialTicks = Timer.RenderPartialTicks;
                        Timer.UpdateTimer();
                        Timer.RenderPartialTicks = previousRenderPartialTicks;
                    }
                    else
                    {
                        Timer.UpdateTimer();
                    }

                    var imguiThisFrame = Options.ShowDebugInfo;
                    var imguiFramebufferScale = Vector2.One;
                    if (imguiThisFrame)
                    {
                        ImGuiImplGLFW.NewFrame();

                        unsafe
                        {
                            ImGuiIO* io = ImGui.GetIO();
                            var w = Math.Max(1, Display.getWidth());
                            var h = Math.Max(1, Display.getHeight());
                            io->DisplaySize = new Vector2(w, h);
                            io->DisplayFramebufferScale = new Vector2(
                                Display.getFramebufferWidth() / (float)w,
                                Display.getFramebufferHeight() / (float)h);
                            imguiFramebufferScale = io->DisplayFramebufferScale;
                        }

                        ImGui.NewFrame();
                        ImGuiInput.CapturingKeyboard = ImGui.GetIO().WantCaptureKeyboard
                                                       && !_debugWindowManager.GameViewportFocused
                                                       && !InGameHasFocus
                                                       && CurrentScreen == null;
                    }
                    else
                    {
                        ImGuiInput.CapturingKeyboard = false;
                    }

                    var tickStartTime = Stopwatch.GetTimestamp();

                    using (Profiler.Begin("Ticks"))
                    {
                        for (var tickIndex = 0; tickIndex < Timer.ElapsedTicks; ++tickIndex)
                        {
                            ++TicksRan;
                            RunTick(Timer.RenderPartialTicks);
                        }
                    }

                    var tickElapsedTime = Stopwatch.GetTimestamp() - tickStartTime;

                    EntityBaseline?.PrepareFrame();

                    SoundManager.UpdateListener(Player, Timer.RenderPartialTicks);

                    if (!Keyboard.isKeyDown(Keyboard.KEY_F7))
                    {
                        using (Profiler.Begin("DisplayPresent"))
                        {
                            Display.update();
                        }
                    }

                    if (Player != null && Player.IsInsideWall())
                    {
                        Options.CameraMode = CameraMode.FirstPerson;
                    }

                    int savedWidth = DisplayWidth, savedHeight = DisplayHeight;

                    // ImGui owns the debug viewport's layout, while WebGPU composites the game
                    // directly into the resulting swapchain rectangle.
                    if (imguiThisFrame)
                    {
                        // Sizing happens below instead, after DebugWindowManager.Render() runs —
                        // see the imgui-build block. Deciding it here, before Render() has produced
                        // this frame's panel size, is what caused the resize to permanently lag the
                        // Image widget's requested size by one step: RenderFrame() would resize the
                        // offscreen texture to a size one frame older than the one ImGui.Render() had
                        // already baked into this same frame's draw data. Since this block runs every
                        // frame, that lag never had a frame where the two sizes lined up to close it.
                    }
                    else
                    {
                        WebGpuRenderer.ViewportSize = null;
                        WebGpuRenderer.ViewportPosition = null;
                        DebugViewportOffset = Vector2.Zero;
                    }

                    // WebGPU builds and submits ImGui draw data as one of the passes recorded inside
                    // RenderFrame() below, so ImGui must finish layout first. That same layout gives
                    // the renderer the exact swapchain rectangle it fills before drawing the panels.
                    if (imguiThisFrame)
                    {
                        using (Profiler.Begin("ImguiBuild"))
                        {
                            _debugWindowManager.Render(Timer.DeltaTime);
                        }

                        // Read the rectangle Render() just produced, not a value from the previous
                        // frame. The offscreen render size and the swapchain viewport must follow
                        // the exact same layout result so a dock resize cannot create a gap.
                        var vpSize = _debugWindowManager.ViewportSize;
                        if (vpSize.X > 0 && vpSize.Y > 0)
                        {
                            int vpW = (int)vpSize.X, vpH = (int)vpSize.Y;
                            WebGpuRenderer.ViewportSize = (
                                (uint)Math.Max(1, (int)MathF.Round(vpSize.X * imguiFramebufferScale.X)),
                                (uint)Math.Max(1, (int)MathF.Round(vpSize.Y * imguiFramebufferScale.Y)));
                            WebGpuRenderer.ViewportPosition = (
                                (uint)Math.Max(0, (int)MathF.Round(
                                    _debugWindowManager.ViewportPos.X * imguiFramebufferScale.X)),
                                (uint)Math.Max(0, (int)MathF.Round(
                                    _debugWindowManager.ViewportPos.Y * imguiFramebufferScale.Y)));
                            DisplayWidth = vpW;
                            DisplayHeight = vpH;

                            DebugViewportOffset = new Vector2(
                                _debugWindowManager.ViewportPos.X,
                                Display.getHeight() - vpH - _debugWindowManager.ViewportPos.Y);
                        }
                        else
                        {
                            WebGpuRenderer.ViewportSize = null;
                            WebGpuRenderer.ViewportPosition = null;
                            DebugViewportOffset = Vector2.Zero;
                        }

                        using (Profiler.Begin("ImguiSubmit"))
                        {
                            ImGui.Render();
                        }
                    }

                    if (!SkipRenderWorld)
                    {
                        PlayerController?.SetPartialTime(Timer.RenderPartialTicks);

                        TextureStats.StartFrame();

                        using (Profiler.Begin("Render"))
                        {
                            WebGpuRenderer.ImguiOpen = imguiThisFrame;
                            WebGpuRenderer.RenderFrame(Timer.RenderPartialTicks,
                                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                        }

                        TextureStats.EndFrame();
                        PushRenderMetrics();
                    }

                    DisplayWidth = savedWidth;
                    DisplayHeight = savedHeight;

                    if (!Display.isActive())
                    {
                        if (_fullscreen) ToggleFullscreen();
                        if (Options.PauseOnFocusLossOption.Value) Thread.Sleep(10);
                    }

                    _prevFrameTime = Stopwatch.GetTimestamp();

                    if (Keyboard.isKeyDown(Keyboard.KEY_F7))
                    {
                        Display.update();
                    }

                    ScreenshotListener();

                    if (Display.wasResized())
                    {
                        DisplayWidth = Display.getWidth();
                        DisplayHeight = Display.getHeight();
                        if (DisplayWidth <= 0) DisplayWidth = 1;
                        if (DisplayHeight <= 0) DisplayHeight = 1;
                        Resize(DisplayWidth, DisplayHeight);
                    }

                    ++frameCounter;

                    IsGamePaused = (!IsMultiplayerWorld() || InternalServer != null) && (CurrentScreen?.PausesGame ?? false);

                    for (; DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= lastFpsCheckTime + 1000L; frameCounter = 0)
                    {
                        DebugText = frameCounter + " fps";
                        MetricRegistry.Set(ClientMetrics.Fps, frameCounter);
                        lastFpsCheckTime += 1000L;
                    }

                    _framePacer.WaitUntilFrameBudget(frameStartNano, Options.MaxFramesPerSecond);
                }
                catch (OutOfMemoryException)
                {
                    CrashCleanup();
                    Navigate(new ErrorScreen(UIContext, "Out of memory!", "Minecraft has run out of memory."));
                }
                finally
                {
                    ReportFrameTelemetry(frameStartNano);
                }
            }
        }
        catch (OmniBlockShutdownException)
        {
        }
        catch (Exception unexpectedException)
        {
            CrashCleanup();
            OnGameCrash(unexpectedException);
        }
        finally
        {
            ShutdownGame();
        }
    }

    private void PushRenderMetrics()
    {
        if (WorldRenderer?.ChunkRenderer is not { } cr) return;
        MetricRegistry.Set(RenderMetrics.ChunksTotal, cr.TotalChunks);
        MetricRegistry.Set(RenderMetrics.ChunksFrustum, cr.ChunksInFrustum);
        MetricRegistry.Set(RenderMetrics.ChunksOccluded, cr.ChunksOccluded);
        MetricRegistry.Set(RenderMetrics.ChunksRendered, cr.ChunksRendered);
        MetricRegistry.Set(RenderMetrics.GeometryUploads, cr.GeometryUploadsLastFrame);
        MetricRegistry.Set(RenderMetrics.LightUploads, cr.LightUploadsLastFrame);
        MetricRegistry.Set(RenderMetrics.SolidDraws, cr.SolidDrawsLastFrame);
        MetricRegistry.Set(RenderMetrics.TranslucentDraws, cr.TranslucentDrawsLastFrame);
        var presentation = cr.PresentationProfile;
        MetricRegistry.Set(RenderMetrics.VisibilityCandidates, presentation.VisibilityCandidates);
        MetricRegistry.Set(RenderMetrics.SpatialRegionTests, presentation.SpatialRegionTests);
        MetricRegistry.Set(RenderMetrics.SpatialColumnTests, presentation.SpatialColumnTests);
        MetricRegistry.Set(RenderMetrics.SpatialSectionTests, presentation.SpatialSectionTests);
        MetricRegistry.Set(RenderMetrics.FrustumTests, presentation.FrustumTests);
        MetricRegistry.Set(RenderMetrics.PortalVisited, presentation.PortalVisited);
        MetricRegistry.Set(RenderMetrics.DisconnectedSeeds, presentation.DisconnectedSeeds);
        MetricRegistry.Set(RenderMetrics.SafetyRescued, presentation.SafetyRescued);
        MetricRegistry.Set(RenderMetrics.IncompleteAdjacencyRescued, presentation.IncompleteAdjacencyRescued);
        MetricRegistry.Set(RenderMetrics.NewPresentationRescued, presentation.NewPresentationRescued);
        MetricRegistry.Set(RenderMetrics.PresentationRegressionRescued, presentation.PresentationRegressionRescued);
        MetricRegistry.Set(RenderMetrics.OldestSafetyRescueFrames, presentation.OldestSafetyRescueFrames);
        MetricRegistry.Set(RenderMetrics.ResidentSolidLayers, presentation.ResidentSolidLayers);
        MetricRegistry.Set(RenderMetrics.ResidentTranslucentLayers, presentation.ResidentTranslucentLayers);
        MetricRegistry.Set(RenderMetrics.PresentedSolidLayers, presentation.PresentedSolidLayers);
        MetricRegistry.Set(RenderMetrics.PresentedTranslucentLayers, presentation.PresentedTranslucentLayers);
        MetricRegistry.Set(RenderMetrics.EmptyLayersSubmitted, presentation.EmptyLayersSubmitted);
        MetricRegistry.Set(RenderMetrics.TerrainDrawCalls, presentation.TerrainDrawCalls);
        MetricRegistry.Set(RenderMetrics.AvailableQuads, presentation.AvailableQuads);
        MetricRegistry.Set(RenderMetrics.SubmittedQuads, presentation.SubmittedQuads);
        MetricRegistry.Set(RenderMetrics.DirectionRejectedQuads, presentation.DirectionRejectedQuads);
        MetricRegistry.Set(RenderMetrics.DirectionDrawRanges, presentation.DirectionDrawRanges);
        MetricRegistry.Set(RenderMetrics.UnassignedQuads, presentation.UnassignedQuads);
        MetricRegistry.Set(RenderMetrics.TerrainUniformEntries, presentation.TerrainUniformEntries);
        MetricRegistry.Set(RenderMetrics.TerrainSubmissionBatches, presentation.TerrainSubmissionBatches);
        MetricRegistry.Set(RenderMetrics.TerrainPipelineBinds, presentation.TerrainPipelineBinds);
        MetricRegistry.Set(RenderMetrics.TerrainTextureBinds, presentation.TerrainTextureBinds);
        MetricRegistry.Set(RenderMetrics.TerrainUniformArenaCapacity, presentation.TerrainUniformArenaCapacity);
        MetricRegistry.Set(RenderMetrics.TerrainUniformArenaGrowths, presentation.TerrainUniformArenaGrowths);
        MetricRegistry.Set(RenderMetrics.FindVisibleMs, presentation.FindVisible.LastMs);
        MetricRegistry.Set(RenderMetrics.TerrainSubmitCpuMs, presentation.TerrainSubmit.LastMs);
        MetricRegistry.Set(RenderMetrics.MeshVersionAllocated, ChunkMeshVersion.TotalAllocated);
        MetricRegistry.Set(RenderMetrics.MeshVersionReleased, ChunkMeshVersion.TotalReleased);
        MetricRegistry.Set(RenderMetrics.TextureBindsLastFrame, TextureStats.BindsLastFrame);
        MetricRegistry.Set(RenderMetrics.TextureAvgBinds, (float)TextureStats.AverageBindsPerFrame);
        MetricRegistry.Set(RenderMetrics.TextureActive, Texture2D.ActiveTextureCount);
        MetricRegistry.Set(RenderMetrics.EntitiesRendered, WorldRenderer.CountEntitiesRendered);
        MetricRegistry.Set(RenderMetrics.EntitiesHidden, WorldRenderer.CountEntitiesHidden);
        MetricRegistry.Set(RenderMetrics.EntitiesTotal, WorldRenderer.CountEntitiesTotal);
        MetricRegistry.Set(RenderMetrics.ParticlesActive, ParticleManager.ActiveParticleCount);
        MetricRegistry.Set(RenderMetrics.ParticlesRendered, ParticleManager.RenderedParticleCount);
        MetricRegistry.Set(RenderMetrics.ParticlesHidden, ParticleManager.HiddenParticleCount);
        MetricRegistry.Set(RenderMetrics.BlockEntitiesTotal, WorldRenderer.CountBlockEntitiesTotal);
        MetricRegistry.Set(RenderMetrics.BlockEntitiesRendered, WorldRenderer.CountBlockEntitiesRendered);
        MetricRegistry.Set(RenderMetrics.BlockEntitiesHidden, WorldRenderer.CountBlockEntitiesHidden);
        MetricRegistry.Set(RenderMetrics.PresentationQuality, Options.PresentationQuality);
    }

    private void ReportFrameTelemetry(long frameStartNano)
    {
        var frameEndNano = Stopwatch.GetTimestamp();
        var thisFrameTimeMs = Stopwatch.GetElapsedTime(frameStartNano, frameEndNano).TotalMilliseconds;
        EntityBaseline?.RecordFrame(thisFrameTimeMs);
        _debugTelemetry.RecordFrameTime(thisFrameTimeMs);
        MetricRegistry.Set(ClientMetrics.FrameTimeMs, (float)thisFrameTimeMs);
        InternalServer?.ReportIntegratedClientFrameTime(thisFrameTimeMs);

        Profiler.Record("FrameTime", thisFrameTimeMs);
        Profiler.CaptureFrame();
    }

    #endregion

    #region Tick Logic

    public void RunTick(float partialTicks)
    {
        CommitPendingContent();
        using var _tick = Profiler.Begin("Tick");

        // Per docs/luau-persistent-lifecycle-plan.md §3/§4: reset the instruction budget and
        // pace GC once per tick, before any Host-phase call this tick would be allowed to run —
        // a per-tick ceiling shared across every script invocation that tick, not per-call. No
        // Host facade calls into this VM yet (docs/luau-ui-host-api-plan.md §3 isn't built), so
        // this is currently inert plumbing, not something a script can observe yet.
        if (LuauState is { } luauState)
        {
            using (Profiler.Begin("LuauTick"))
            {
                luauState.ResetInstructionBudget(LuauInstructionBudgetPerTick);
                luauState.StepGarbageCollector(LuauGcStepKb);
                if (!_luauSchedulerFailed &&
                    !LuauScheduler.Tick(luauState, 1.0 / Timer.TicksPerSecond, out var schedulerError))
                {
                    // A task can consume the shared budget and abort this tick's scheduler call.
                    // The budget is reset above on the next tick, so keep the scheduler alive.
                    _logger.LogWarning("Luau scheduler tick aborted: {Error}", schedulerError);
                }
            }
        }

        ProcessPendingLuauWorldLoad();

        using (Profiler.Begin("SyncStats"))
        {
            StatFileWriter.SyncStatsIfReady();
        }

        var f11Down = Keyboard.isKeyDown(Keyboard.KEY_F11);
        if (f11Down && !_prevF11Down)
        {
            ToggleFullscreen();
        }

        _prevF11Down = f11Down;

        // F3 uses edge detection so it works even when
        // CurrentScreen.HandleInput() has already consumed all keyboard events.
        var f3Down = Keyboard.isKeyDown(Keyboard.KEY_F3);
        if (f3Down && !_prevF3Down)
        {
            Options.ShowDebugInfo = !Options.ShowDebugInfo;

            // The overlay needs a visible cursor to be usable at all: while InGameHasFocus is
            // true, DebugWindowManager sets ImGuiConfigFlags.NoMouse and ImGui ignores the mouse
            // entirely. Every other path that drops in-game focus goes through Navigate(screen),
            // which opens (and pauses behind) a game screen. Releasing here is what makes the
            // one state where the debug overlay is up with no screen behind it reachable at all;
            // closing the overlay re-grabs.
            if (CurrentScreen == null && World != null)
            {
                if (Options.ShowDebugInfo)
                {
                    SetIngameNotInFocus();
                }
                else
                {
                    SetIngameFocus();
                }
            }
        }

        _prevF3Down = f3Down;

        ControllerManager.UpdateGlobal();

        if (!InGameHasFocus && World == null && InternalServer == null)
        {
            if (Options.MenuMusic)
            {
                SoundManager.PlayRandomMusicIfReady(DefaultMusicCategories.Menu);
            }
            else
            {
                SoundManager.StopMusic(DefaultMusicCategories.Menu);
            }
        }

        using (Profiler.Begin("UpdateHud"))
        {
            HUD.Update(1.0f);
        }

        GameRenderer.UpdateTargetedEntity(1.0F);
        GameRenderer.Tick(partialTicks);

        using (Profiler.Begin("UpdatePlayerController"))
        {
            if (!IsGamePaused && World != null)
            {
                PlayerController.UpdateController();
            }
        }

        using (Profiler.Begin("UpdateDynamicTextures"))
        {
            using (Profiler.Begin("BindAtlas"))
            {
                TextureManager.BindTexture(TextureManager.GetTextureId("/terrain.png"));
            }

            if (!IsGamePaused)
            {
                using (Profiler.Begin("TickTextures"))
                {
                    TextureManager.Tick();
                }
            }
        }

        if (CurrentScreen == null && Player != null)
        {
            if (Player.Health <= 0)
            {
                Navigate(null);
            }
            else if (Player.IsSleeping && World != null && World.IsRemote)
            {
                Navigate(new SleepScreen(UIContext, Player));
            }
        }
        else if (CurrentScreen is SleepScreen && !Player.IsSleeping)
        {
            Navigate(null);
        }

        if (CurrentScreen != null)
        {
            _leftClickCounter = 10000;
            MouseTicksRan = TicksRan + 10000;
        }

        if (CurrentScreen != null)
        {
            CurrentScreen.HandleInput();
            CurrentScreen?.Update(1.0f);
        }

        if (CurrentScreen == null || CurrentScreen.AllowUserInput)
        {
            ProcessInputEvents();
        }

        if (World != null)
        {
            if (Player != null)
            {
                ++_joinPlayerCounter;
                if (_joinPlayerCounter == 30)
                {
                    _joinPlayerCounter = 0;
                    World.Entities.LoadChunksNearEntity(Player);
                }
            }

            World.SetDifficulty(Options.Difficulty);
            InternalServer?.SetDifficulty(Options.Difficulty);

            if (World.IsRemote)
            {
                World.SetDifficulty(3);
            }

            using (Profiler.Begin("UpdateEntityRenderer"))
            {
                if (!IsGamePaused)
                {
                    GameRenderer.UpdateCamera();
                }
            }

            if (!IsGamePaused)
            {
                WorldRenderer.UpdateClouds();
            }

            using (Profiler.Begin("TickEntities"))
            {
                if (!IsGamePaused)
                {
                    if (World.Environment.LightningTicksLeft > 0)
                    {
                        --World.Environment.LightningTicksLeft;
                    }

                    // Before the tick, not after: this sets each entity's target for the tick and
                    // TickMovement consumes it during the tick, which is where the animation delta
                    // and the renderer's interpolation interval are both derived from.
                    if (World is ClientWorld clientWorld)
                    {
                        clientWorld.NetworkHandler.ApplyInterpolation(World);
                    }

                    World.Entities.TickEntities();
                }
            }

            using (Profiler.Begin("TickWorld"))
            {
                if (!IsGamePaused || (IsMultiplayerWorld() && InternalServer == null))
                {
                    World.allowSpawning(Options.Difficulty > 0, true);
                    World.Tick();
                }
            }

            if (!IsGamePaused && World != null)
            {
                World.displayTick(MathHelper.Floor(Player.X), MathHelper.Floor(Player.Y), MathHelper.Floor(Player.Z));
            }

            if (!IsGamePaused)
            {
                ParticleManager.updateEffects();
            }
        }

        _systemTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private void ProcessPendingLuauWorldLoad()
    {
        if (_testDisconnectRequested)
        {
            _testDisconnectRequested = false;
            if (IsMultiplayerWorld()) World?.Disconnect();
            StopInternalServer();
            ChangeWorld(null);
            Navigate(null);
        }
        if (_luauWorldService?.TryTakePending(out var request) != true || request == null)
            return;

        if (World != null || InternalServer != null)
        {
            _logger.LogWarning("Discarded scripted world load for {WorldId}: client is no longer at the main menu", request.Id);
            return;
        }

        _logger.LogInformation("Luau requested single-player world load: {WorldId}", request.Id);
        LoadWorld(request.Id, request.DisplayName, request.Settings);
    }

    #endregion

    #region Input Handling

    private void ProcessInputEvents()
    {
        while (Mouse.next())
        {
            var timeSinceLastMouseEvent = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _systemTime;

            if (Mouse.getEventDX() != 0 || Mouse.getEventDY() != 0)
            {
                IsControllerMode = false;
                Mouse.setCursorVisible(true);
            }

            if (timeSinceLastMouseEvent <= 200L)
            {
                var mouseWheelDelta = Mouse.getEventDWheel();
                if (mouseWheelDelta != 0)
                {
                    IsControllerMode = false;
                    Mouse.setCursorVisible(true);

                    var zoomHeld = CurrentScreen == null && InGameHasFocus && Keyboard.isKeyDown(Options.KeyBindZoom.ScanCode);
                    if (zoomHeld)
                    {
                        var mouseWheelDirection = mouseWheelDelta > 0 ? 1 : -1;
                        if (mouseWheelDirection > 0)
                        {
                            Options.ZoomScale *= 1.08F;
                        }
                        else
                        {
                            Options.ZoomScale /= 1.08F;
                        }

                        Options.ZoomScale = Math.Clamp(Options.ZoomScale, 1.25F, 20.0F);
                    }
                    // Same rule as clicks: with the cursor freed by the overlay, a wheel event over
                    // a debug window is the overlay's to scroll, not a hotbar change.
                    else if (!Options.ShowDebugInfo || InGameHasFocus || _debugWindowManager.GameViewportFocused)
                    {
                        Player.Inventory.ChangeCurrentItem(mouseWheelDelta);
                        if (Options.InvertScrolling)
                        {
                            if (mouseWheelDelta > 0) mouseWheelDelta = 1;
                            if (mouseWheelDelta < 0) mouseWheelDelta = -1;
                            Options.AmountScrolled += mouseWheelDelta * 0.25F;
                        }
                    }
                }

                if (CurrentScreen == null)
                {
                    if (!InGameHasFocus)
                    {
                        // The cursor is free. A click only means "back into the game" when it lands
                        // on the game viewport — re-grabbing on any click would swallow the first
                        // click on every debug widget, and letting it fall through to ClickMouse
                        // would swing the held item at whatever is behind the overlay.
                        var clickTargetsGame = !Options.ShowDebugInfo || _debugWindowManager.GameViewportFocused;
                        if (Mouse.getEventButtonState() && clickTargetsGame)
                        {
                            SetIngameFocus();
                        }
                    }
                    else
                    {
                        if (Mouse.getEventButton() == 0 && Mouse.getEventButtonState())
                        {
                            ClickMouse(0);
                            MouseTicksRan = TicksRan;
                        }

                        if (Mouse.getEventButton() == 1 && Mouse.getEventButtonState())
                        {
                            ClickMouse(1);
                            MouseTicksRan = TicksRan;
                        }

                        if (Mouse.getEventButton() == 2 && Mouse.getEventButtonState())
                        {
                            ClickMiddleMouseButton();
                        }
                    }
                }
                else
                {
                    CurrentScreen?.HandleMouseInput();
                }
            }
        }

        if (_leftClickCounter > 0)
        {
            --_leftClickCounter;
        }

        while (Keyboard.Next())
        {
            // Block key-down events when ImGui has keyboard focus.
            if (ImGuiInput.CapturingKeyboard)
            {
                continue;
            }

            Player?.handleKeyPress(Keyboard.getEventKey(), Keyboard.getEventKeyState());

            if (Keyboard.getEventKeyState())
            {
                if (CurrentScreen != null)
                {
                    CurrentScreen.HandleKeyboardInput();
                }
                else
                {
                    if (Keyboard.getEventKey() == Keyboard.KEY_ESCAPE) DisplayInGameMenu();

                    if (Keyboard.getEventKey() == Keyboard.KEY_S && Keyboard.isKeyDown(Keyboard.KEY_F3))
                    {
                        ForceReload();
                    }

                    if (Keyboard.getEventKey() == Keyboard.KEY_H && Keyboard.isKeyDown(Keyboard.KEY_F3))
                    {
                        Options.AdvancedItemTooltips = !Options.AdvancedItemTooltips;
                        Options.SaveOptions();
                    }

                    if (Keyboard.getEventKey() == Keyboard.KEY_D && Keyboard.isKeyDown(Keyboard.KEY_F3))
                    {
                        HUD.Chat.ClearMessages();
                    }

                    if (Keyboard.getEventKey() == Keyboard.KEY_C && Keyboard.isKeyDown(Keyboard.KEY_F3))
                    {
                        throw new Exception("Simulated crash triggered by pressing F3 + C");
                    }

                    if (Keyboard.getEventKey() == Keyboard.KEY_F1) Options.HideGUI = !Options.HideGUI;

                    if (Keyboard.getEventKey() == Keyboard.KEY_F5)
                    {
                        Options.CameraMode = (CameraMode)((int)(Options.CameraMode + 2) % 3);
                    }

                    if (Keyboard.getEventKey() == Keyboard.KEY_F8) Options.SmoothCamera = !Options.SmoothCamera;
                    if (Keyboard.getEventKey() == Keyboard.KEY_F7) ShowChunkBorders = !ShowChunkBorders;

                    if (Keyboard.getEventKey() == Options.KeyBindInventory.ScanCode)
                    {
                        Navigate(new InventoryScreen(UIContext, Player, PlayerController, () => CurrentScreen));
                    }

                    if (Keyboard.getEventKey() == Options.KeyBindDrop.ScanCode) Player.DropSelectedItem();

                    if (Keyboard.getEventKey() == Options.KeyBindChat.ScanCode)
                    {
                        Navigate(new ChatScreen(UIContext, HUD.Chat, Player));
                    }

                    if (Keyboard.getEventKey() == Options.KeyBindCommand.ScanCode)
                    {
                        Navigate(new ChatScreen(UIContext, HUD.Chat, Player, "/"));
                    }
                }

                for (var slotIndex = 0; slotIndex < 9; ++slotIndex)
                {
                    if (Keyboard.getEventKey() == Keyboard.KEY_1 + slotIndex)
                    {
                        Player.Inventory.SelectedSlot = slotIndex;
                    }
                }

                if (Keyboard.getEventKey() == Options.KeyBindToggleFog.ScanCode)
                {
                    Options.RenderDistanceOption.Value = Math.Clamp(
                        Options.RenderDistanceOption.Value + (!Keyboard.isKeyDown(Keyboard.KEY_LSHIFT) && !Keyboard.isKeyDown(Keyboard.KEY_RSHIFT) ? 1.0f / 28.0f : -1.0f / 28.0f),
                        0.0f,
                        1.0f);
                }
            }
        }


        ControllerManager.UpdateUI(CurrentScreen);
        ControllerManager.UpdateInGame(Timer.RenderPartialTicks);

        if (CurrentScreen == null)
        {
            if (Mouse.isButtonDown(0) && TicksRan - MouseTicksRan >= Timer.TicksPerSecond / 4.0F && InGameHasFocus)
            {
                ClickMouse(0);
                MouseTicksRan = TicksRan;
            }

            if (Mouse.isButtonDown(1) && TicksRan - MouseTicksRan >= Timer.TicksPerSecond / 4.0F && InGameHasFocus)
            {
                ClickMouse(1);
                MouseTicksRan = TicksRan;
            }
        }

        UpdateHeldMouseButton(0, CurrentScreen == null && (Mouse.isButtonDown(0) || Controller.RightTrigger > 0.5f) && InGameHasFocus);
    }

    public void ClickMouse(int mouseButton)
    {
        if (mouseButton != 0 || _leftClickCounter <= 0)
        {
            if (mouseButton == 0)
            {
                Player.SwingHand();
            }

            var shouldPerformSecondaryAction = true;
            if (ObjectMouseOver.Type == HitResultType.Miss)
            {
                if (mouseButton == 0)
                {
                    _leftClickCounter = 10;
                }
            }
            else if (ObjectMouseOver.Type == HitResultType.Entity)
            {
                if (mouseButton == 0)
                {
                    PlayerController.AttackEntity(Player, ObjectMouseOver.Entity);
                }

                if (mouseButton == 1)
                {
                    PlayerController.InteractWithEntity(Player, ObjectMouseOver.Entity);
                }
            }
            else if (ObjectMouseOver.Type == HitResultType.Tile)
            {
                var blockX = ObjectMouseOver.BlockX;
                var blockY = ObjectMouseOver.BlockY;
                var blockZ = ObjectMouseOver.BlockZ;
                var blockSide = ObjectMouseOver.Side;
                if (mouseButton == 0)
                {
                    PlayerController.ClickBlock(blockX, blockY, blockZ, ObjectMouseOver.Side);
                }
                else
                {
                    var selectedItem = Player.Inventory.ItemInHand;
                    var itemCountBefore = selectedItem != null ? selectedItem.Count : 0;
                    if (PlayerController.SendPlaceBlock(Player, World, selectedItem, blockX, blockY, blockZ, blockSide))
                    {
                        shouldPerformSecondaryAction = false;
                        Player.SwingHand();
                    }

                    if (selectedItem == null)
                    {
                        return;
                    }

                    if (selectedItem.Count == 0)
                    {
                        Player.Inventory.Main[Player.Inventory.SelectedSlot] = null;
                    }
                    else if (selectedItem.Count != itemCountBefore)
                    {
                        GameRenderer.ItemRenderer.ResetEquippedProgress();
                    }
                }
            }

            if (shouldPerformSecondaryAction && mouseButton == 1)
            {
                var selectedItem = Player.Inventory.ItemInHand;
                if (selectedItem != null && PlayerController.SendUseItem(Player, World, selectedItem))
                {
                    GameRenderer.ItemRenderer.ResetEquippedProgress();
                }
            }
        }
    }

    public void ClickMiddleMouseButton()
    {
        if (ObjectMouseOver.Type != HitResultType.Miss)
        {
            var blockId = World.Reader.GetBlockId(ObjectMouseOver.BlockX, ObjectMouseOver.BlockY, ObjectMouseOver.BlockZ);
            var blockMeta = World.Reader.GetBlockMeta(ObjectMouseOver.BlockX, ObjectMouseOver.BlockY, ObjectMouseOver.BlockZ);
            var hitBlock = Content.Blocks.GetByProtocolId(blockId);

            var (primaryMeta, backupId, backupMeta) = hitBlock.GetPickBlockItem(blockMeta);

            Player.Inventory.SetCurrentItem(blockId, backupId, primaryMeta, backupMeta);
        }
    }

    private void UpdateHeldMouseButton(int mouseButton, bool isHoldingMouse)
    {
        if (!PlayerController.IsTestPlayer)
        {
            if (!isHoldingMouse)
            {
                _leftClickCounter = 0;
            }

            if (mouseButton != 0 || _leftClickCounter <= 0)
            {
                if (isHoldingMouse && ObjectMouseOver.Type != HitResultType.Miss && ObjectMouseOver.Type == HitResultType.Tile &&
                    mouseButton == 0)
                {
                    var blockX = ObjectMouseOver.BlockX;
                    var blockY = ObjectMouseOver.BlockY;
                    var blockZ = ObjectMouseOver.BlockZ;
                    PlayerController.SendBlockRemoving(blockX, blockY, blockZ, ObjectMouseOver.Side);
                    ParticleManager.addBlockHitEffects(blockX, blockY, blockZ, ObjectMouseOver.Side);
                }
                else
                {
                    PlayerController.ResetBlockRemoving();
                }
            }
        }
    }

    #endregion

    #region World & Server Operations

    public void LoadWorld(string dir, string displayName, WorldSettings settings)
    {
        StatFileWriter.ReadStat(Stats.Stats.LoadWorldStat, 1);
        StartWorld(dir, displayName, settings);
    }

    public void StartWorld(string worldName, string mainMenuText, WorldSettings settings)
    {
        ChangeWorld(null);
        _singleplayerWorldId = worldName;
        Navigate(new LevelLoadingScreen(UIContext, CreateNetworkContext(), worldName, settings, this));
    }

    public void ChangeWorld(World? newWorld, string loadingText = "", EntityPlayer? targetEntity = null)
    {
        StatFileWriter.Tick();
        StatFileWriter.SyncStats();
        _loadingScreen.BeginLoading(loadingText);
        _loadingScreen.SetStage("");
        SoundManager.PlayStreaming(null!, 0.0F, 0.0F, 0.0F, 0.0F, 0.0F);

        World = newWorld!;
        if (newWorld != null)
        {
            PlayerController.ChangeWorld(newWorld);
            if (!IsMultiplayerWorld())
            {
                if (targetEntity == null)
                {
                    Player = (ClientPlayerEntity?)World.GetPlayerForProxy(typeof(ClientPlayerEntity));
                }
            }
            else if (Player != null)
            {
                Player.TeleportToTop();
                newWorld?.Entities.SpawnEntity(Player);
            }

            if (Player == null)
            {
                Player = (ClientPlayerEntity)PlayerController.CreatePlayer(newWorld);
                Player.TeleportToTop();
                PlayerController.FlipPlayer(Player);
            }

            Player.movementInput = new MovementInputFromOptions(Options);
            WorldRenderer?.ChangeWorld(newWorld);
            ParticleManager?.clearEffects(newWorld);

            PlayerController.FillHotbar(Player);
            if (targetEntity != null)
            {
                World.SaveWorldData();
            }

            newWorld.AddPlayer(Player);
            SkinManager.RequestDownload(Player.Name);

            if (newWorld.IsNewWorld)
            {
                newWorld.SavingProgress(_loadingScreen);
            }
        }
        else
        {
            WorldRenderer?.ChangeWorld(null!);
            ParticleManager?.clearEffects(null!);
            Player = null;
        }

        _systemTime = 0L;
    }

    public void Respawn(bool ignoreSpawnPosition, int newDimensionId)
    {
        Vec3I? playerSpawnPos = null;
        Vec3I? respawnPos = null;

        if (Player is not null && !ignoreSpawnPosition)
        {
            playerSpawnPos = Player.GetSpawnPos();

            if (playerSpawnPos is not null)
            {
                respawnPos = EntityPlayer.FindRespawnPosition(World, playerSpawnPos);

                if (respawnPos is null)
                {
                    Player.SendMessage("tile.bed.notValid");
                }
            }
        }

        var useBedSpawn = respawnPos is not null;
        var finalRespawnPos = respawnPos ?? World.Properties.GetSpawnPos();

        World.UpdateSpawnPosition();
        World.Entities.UpdateEntityLists();

        var previousPlayerId = 0;
        Holder<GameMode>? previousGameModeHolder = null;

        if (Player is not null)
        {
            previousPlayerId = Player.ID;
            previousGameModeHolder = Player.GameModeHolder;
            World.Entities.Remove(Player);
        }

        Player = (ClientPlayerEntity)PlayerController.CreatePlayer(World);
        Player.DimensionId = newDimensionId;
        Player.TeleportToTop();

        if (previousGameModeHolder is not null)
        {
            Player.GameModeHolder = previousGameModeHolder;
        }

        if (useBedSpawn)
        {
            Player.SetSpawnPos(playerSpawnPos);
            Player.SetPositionAndAnglesKeepPrevAngles(
                finalRespawnPos.X + 0.5,
                finalRespawnPos.Y + 0.1,
                finalRespawnPos.Z + 0.5,
                0.0F,
                0.0F);
        }

        PlayerController.FlipPlayer(Player);
        World.AddPlayer(Player);
        Player.movementInput = new MovementInputFromOptions(Options);
        Player.ID = previousPlayerId;
        Player.Spawn();
        PlayerController.FillHotbar(Player);

        ShowText("Respawning");

        if (_isGameOverOpen)
        {
            Navigate(null);
        }
    }

    public void StartInternalServer(string worldDir, WorldSettings worldSettings)
    {
        InternalServer = new InternalServer(
            Path.Combine(OmniBlockDir, "saves"), worldDir, worldSettings,
            Options.RenderDistance, Options.SimulationDistance, Options.Difficulty, Content,
            TerrainLodPolicy);
        InternalServer.RegistryAccess = RegistryAccess;
        InternalServer.RunThreaded("Internal Server");
    }

    private void StopInternalServer()
    {
        if (InternalServer == null)
        {
            _singleplayerWorldId = null;
            return;
        }

        InternalServer.Stop();
        while (!InternalServer.stopped)
        {
            Thread.Sleep(1);
        }

        InternalServer = null;
        _singleplayerWorldId = null;
    }

    private bool IsMultiplayerWorld() => World is { IsRemote: true };

    private void ShowText(string loadingText)
    {
        _loadingScreen.BeginLoading(loadingText);
        _loadingScreen.SetStage("Building terrain");
        short loadingRadius = 128;
        var loadedChunkCount = 0;
        var totalChunksToLoad = loadingRadius * 2 / 16 + 1;
        totalChunksToLoad *= totalChunksToLoad;
        var centerPos = World.Properties.GetSpawnPos();

        if (Player != null)
        {
            centerPos.X = (int)Player.X;
            centerPos.Z = (int)Player.Z;
        }

        for (var xOffset = -loadingRadius; xOffset <= loadingRadius; xOffset += 16)
        {
            for (var zOffset = -loadingRadius; zOffset <= loadingRadius; zOffset += 16)
            {
                _loadingScreen.SetProgress(loadedChunkCount++ * 100 / totalChunksToLoad);
                World.Reader.GetBlockId(centerPos.X + xOffset, 64, centerPos.Z + zOffset);
            }
        }

        _loadingScreen.SetStage("Simulating world for a bit");
        World.TickChunks();
    }

    #endregion

    #region UI & Navigation

    public void Navigate(UIScreen? newScreen)
    {
        Mouse.Flush();
        Keyboard.Flush();
        Controller.ClearEvents();
        var oldScreen = CurrentScreen;
        oldScreen?.Uninit();

        if (newScreen is MainMenuScreen)
        {
            StatFileWriter.Tick();

            if (InGameHasFocus)
            {
                SoundManager.StopCurrentMusic();
            }
        }

        StatFileWriter.SyncStats();
        if (newScreen == null)
        {
            if (World == null)
            {
                newScreen = CreateMainMenuScreen();
            }
            else if (Player.Health <= 0)
            {
                newScreen = new GameOverScreen(UIContext, Player.getScore(), Player.Respawn, Session != null, () => ChangeWorld(null!));
            }
        }

        if (newScreen is MainMenuScreen)
        {
            HUD.Chat.ClearMessages();
        }

        if (InternalServer != null)
        {
            var shouldPause = newScreen?.PausesGame ?? false;
            if (shouldPause || (CurrentScreen?.PausesGame ?? false))
            {
                InternalServer.Paused = shouldPause;
            }
        }

        CurrentScreen = newScreen;

        if (CurrentScreen != null)
        {
            var inputSizeForReset = UIContext.InputDisplaySize;
            VirtualCursor.Reset(inputSizeForReset.X, inputSizeForReset.Y);
        }

        if (newScreen != null)
        {
            SetIngameNotInFocus();
            newScreen.Initialize();
            SkipRenderWorld = false;
        }
        else
        {
            SetIngameFocus();
            SoundManager.StopMusic(DefaultMusicCategories.Menu);
        }
    }

    public void DisplayInGameMenu()
    {
        if (CurrentScreen != null)
        {
            return;
        }

        var isMp = IsMultiplayerWorld() && InternalServer == null;
        var quitText = isMp ? Translations.Get("menu.disconnect") : Translations.Get("menu.saveAndQuitToTitle");
        var saveStep = 0;
        var integratedServer = InternalServer;
        Navigate(new IngameMenuScreen(UIContext, StatFileWriter, () => Navigate(null), quitText, () =>
        {
            if (IsMultiplayerWorld()) World.Disconnect();
            StopInternalServer();
            ChangeWorld(null);
        }, () => World?.AttemptSaving(saveStep++) ?? false, TexturePackList,
            integratedServer is null
                ? null
                : () => new WorldPreparationScreen(
                    UIContext,
                    CurrentScreen,
                    integratedServer.GetPregenerationSnapshots,
                    integratedServer.GetAutomaticPregenerationSnapshots,
                    (action, id) => integratedServer.QueueCommands(
                        $"worldgen {action} {id}", integratedServer))));
    }

    private IReadOnlyList<LuauWorldGenerationInfo> GetLuauWorldGenerationSnapshots() =>
        InternalServer?.GetPregenerationSnapshots()
            .Select(static snapshot =>
            {
                var definition = snapshot.Definition;
                return new LuauWorldGenerationInfo(
                    definition.Id,
                    definition.World,
                    definition.Dimension,
                    definition.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    definition.GeneratorProfile,
                    definition.GeneratorOptionsHash,
                    definition.ContentFingerprint,
                    definition.CenterChunkX,
                    definition.CenterChunkZ,
                    definition.RadiusChunks,
                    definition.TotalTargets,
                    snapshot.Status.ToString().ToLowerInvariant(),
                    snapshot.NextTarget,
                    snapshot.RemainingTargets,
                    snapshot.PreparedTargets,
                    snapshot.DecoratedTargets,
                    snapshot.SavedTargets,
                    snapshot.SkippedTargets,
                    snapshot.WrittenChunks,
                    snapshot.RetainedBytes,
                    snapshot.PeakRetainedBytes,
                    snapshot.DiskBytes,
                    snapshot.TargetsPerSecond,
                    snapshot.ThrottleReason,
                    snapshot.LastError,
                    definition.CreatedUtc.ToString("O"),
                    snapshot.UpdatedUtc.ToString("O"));
            })
            .ToArray()
        ?? [];

    private LuauWorldGenerationCommandResult QueueLuauWorldGenerationStart(
        string id,
        int dimension,
        int centerChunkX,
        int centerChunkZ,
        int radiusChunks)
    {
        var server = InternalServer;
        if (server == null)
            return new(false, "world-generation jobs can only be controlled by the local server owner");
        if (!IsValidWorldGenerationJobId(id))
            return new(false,
                "job IDs may contain only ASCII letters, digits, '.', '-', and '_' (maximum 64 characters)");
        if (dimension is not (0 or -1))
            return new(false, $"dimension {dimension} does not exist");
        if (radiusChunks is < 0 or > 4096)
            return new(false, "radiusChunks must be between 0 and 4096");
        if (server.GetPregenerationSnapshots().Any(snapshot => snapshot.Definition.Id == id))
            return new(false, $"world-generation job '{id}' already exists");

        server.QueueCommands(
            $"worldgen start {id} area {dimension} {centerChunkX} {centerChunkZ} {radiusChunks}",
            server);
        return new(true);
    }

    private LuauWorldGenerationCommandResult QueueLuauWorldGenerationChange(string action, string id)
    {
        var server = InternalServer;
        if (server == null)
            return new(false, "world-generation jobs can only be controlled by the local server owner");
        if (action is not ("pause" or "resume" or "cancel"))
            return new(false, $"unknown world-generation action '{action}'");
        if (!IsValidWorldGenerationJobId(id))
            return new(false, "invalid world-generation job id");

        // A start and its first lifecycle action may be queued by one Luau turn. The job is not
        // published yet in that case, but FIFO server-command ordering resolves it safely.
        server.QueueCommands($"worldgen {action} {id}", server);
        return new(true);
    }

    private static bool IsValidWorldGenerationJobId(string id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 64 && id.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    public void SetIngameFocus()
    {
        if (!Options.CaptureMouseOption.Value || !Display.isActive())
        {
            return;
        }

        if (InGameHasFocus)
        {
            return;
        }

        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        InGameHasFocus = true;
        MouseHelper.GrabMouseCursor();
        Navigate(null);
        _leftClickCounter = 10000;
        MouseTicksRan = TicksRan + 10000;
    }

    internal void RefreshMouseCapture()
    {
        if (!Options.CaptureMouseOption.Value)
            SetIngameNotInFocus();
        else if (World != null && CurrentScreen == null)
            SetIngameFocus();
    }

    private void SetIngameNotInFocus()
    {
        if (!InGameHasFocus)
        {
            return;
        }

        Player?.resetPlayerKeyState();
        InGameHasFocus = false;
        GCSettings.LatencyMode = GCLatencyMode.Batch;
        MouseHelper.UngrabMouseCursor();
        Mouse.setCursorVisible(!IsControllerMode);
    }

    private MainMenuScreen CreateMainMenuScreen() => new(UIContext, Session, this, CreateNetworkContext(), TexturePackList, Shutdown);
    private ClientNetworkContext CreateNetworkContext() => new(this, this, this, Session, StatFileWriter, ParticleManager, HUD.AddChatMessage, this, Path.Combine(GameDataDir, "chunkcache"), Content, StageContent);

    private void StageContent(ContentRuntime content) =>
        Volatile.Write(ref _pendingContent, content);

    private void CommitPendingContent()
    {
        var candidate = Interlocked.Exchange(ref _pendingContent, null);
        if (candidate is null) return;
        EntityRenderDispatcher.Instance.ConfigureContent(candidate);
        TextureManager.SetImpostorCaptureDependencies(
            EntityRenderDispatcher.Instance.ImpostorTextureDependencies);
        Content = candidate;
        World?.ReplaceContent(candidate);
    }

    #endregion

    #region System Utilities

    private void ToggleFullscreen()
    {
        try
        {
            _fullscreen = !_fullscreen;
            if (_fullscreen)
            {
                _tempDisplayWidth = DisplayWidth;
                _tempDisplayHeight = DisplayHeight;

                Display.setDisplayMode(Display.getDesktopDisplayMode());
                Display.setFullscreen(true);
                DisplayWidth = Display.getDisplayMode().getWidth();
                DisplayHeight = Display.getDisplayMode().getHeight();

                if (DisplayWidth <= 0) DisplayWidth = 1;
                if (DisplayHeight <= 0) DisplayHeight = 1;
            }
            else
            {
                Display.setFullscreen(false);
                if (_tempDisplayWidth > 0 && _tempDisplayHeight > 0)
                {
                    Display.setDisplayMode(new DisplayMode(_tempDisplayWidth, _tempDisplayHeight));
                    DisplayWidth = _tempDisplayWidth;
                    DisplayHeight = _tempDisplayHeight;
                }
                else
                {
                    Display.setDisplayMode(new DisplayMode(854, 480));
                    DisplayWidth = 854;
                    DisplayHeight = 480;
                }

                if (DisplayWidth <= 0) DisplayWidth = 1;
                if (DisplayHeight <= 0) DisplayHeight = 1;

                // Center the window
                var desktopMode = Display.getDesktopDisplayMode();
                var centerX = (desktopMode.getWidth() - DisplayWidth) / 2;
                var centerY = (desktopMode.getHeight() - DisplayHeight) / 2;
                Display.setLocation(centerX, centerY);
            }

            Resize(DisplayWidth, DisplayHeight);
            Display.update();
        }
        catch (Exception displayException)
        {
            _logger.LogError(displayException.ToString());
        }
    }

    private void Resize(int newWidth, int newHeight)
    {
        if (newWidth <= 0) newWidth = 1;
        if (newHeight <= 0) newHeight = 1;

        DisplayWidth = newWidth;
        DisplayHeight = newHeight;
        Mouse.setDisplayDimensions(DisplayWidth, DisplayHeight);

        var framebufferWidth = Display.getFramebufferWidth();
        var framebufferHeight = Display.getFramebufferHeight();

        // The surface does not follow the window on its own, and everything the WebGPU renderer
        // sizes — the offscreen target, the projection, the scissor rectangles — reads it.
        WebGpuDevice.Current!.Configure((uint)framebufferWidth, (uint)framebufferHeight);
    }

    private void ScreenshotListener()
    {
        if (Keyboard.isKeyDown(Keyboard.KEY_F2))
        {
            if (!_isTakingScreenshot)
            {
                _isTakingScreenshot = true;

                // Picked up by the next RenderFrame call, not this one — see
                // WebGpuGameRenderer.ScreenshotRequested for why a same-frame capture is not
                // possible here, and ScreenshotResult below for where the message shows up.
                WebGpuRenderer.ScreenshotRequested = true;
            }
        }
        else
        {
            _isTakingScreenshot = false;
        }

        if (WebGpuRenderer.ScreenshotResult is { } webGpuResult)
        {
            HUD.AddChatMessage(webGpuResult);
            WebGpuRenderer.ScreenshotResult = null;
        }
    }

    private MeshSafetyRingState CurrentMeshSafetyRingState()
    {
        if (WorldRenderer?.ChunkRenderer == null || Player == null) return default;
        return WorldRenderer.ChunkRenderer.GetMeshSafetyRingState(
            new Vector3D<double>(Player.X, Player.Y, Player.Z));
    }

    private void ForceReload()
    {
        _logger.LogInformation("FORCING RELOAD!");
        SoundManager = new SoundManager();
        SoundManager.LoadSoundSettings(Options);
        DefaultMusicCategories.Register(SoundManager);
    }

    public void InstallResource(string resourcePath, FileInfo resourceFile)
    {
        if (!resourceFile.FullName.EndsWith("ogg"))
        {
            //TODO: ADD SUPPORT FOR MUS SFX?
            return;
        }

        var slashIndex = resourcePath.IndexOf("/");
        var category = resourcePath.Substring(0, slashIndex);
        resourcePath = resourcePath.Substring(slashIndex + 1);

        if (category.Equals("sound", StringComparison.OrdinalIgnoreCase))
        {
            SoundManager.AddSound(resourcePath, resourceFile);
        }
        else if (category.Equals("newsound", StringComparison.OrdinalIgnoreCase))
        {
            SoundManager.AddSound(resourcePath, resourceFile);
        }
        else if (category.Equals("streaming", StringComparison.OrdinalIgnoreCase))
        {
            SoundManager.AddStreaming(resourcePath, resourceFile);
        }
        else if (category.Equals("music", StringComparison.OrdinalIgnoreCase))
        {
            SoundManager.AddMusic(DefaultMusicCategories.Game, resourcePath, resourceFile);
        }
        else if (category.Equals("newmusic", StringComparison.OrdinalIgnoreCase))
        {
            SoundManager.AddMusic(DefaultMusicCategories.Game, resourcePath, resourceFile);
        }
        else if (category.Equals("custom", StringComparison.OrdinalIgnoreCase))
        {
            var subSlash = resourcePath.IndexOf("/");
            var subCategory = resourcePath.Substring(0, subSlash);
            resourcePath = resourcePath.Substring(subSlash + 1);

            if (subCategory.Equals("music", StringComparison.OrdinalIgnoreCase))
            {
                SoundManager.AddMusic(DefaultMusicCategories.Menu, resourcePath, resourceFile);
            }
        }
    }

    internal DebugSystemSnapshot DebugSystemSnapshot => _debugTelemetry.SystemSnapshot;

    private void LoadScreen()
    {
        ScaledResolution scaledResolution = new(Options, DisplayWidth, DisplayHeight);
        RenderSystem.Projection.LoadIdentity();
        RenderSystem.Projection.Ortho(0.0D, scaledResolution.ScaledWidth, scaledResolution.ScaledHeight, 0.0D, 1000.0D, 3000.0D);
        RenderSystem.ModelView.LoadIdentity();
        RenderSystem.ModelView.Translate(0.0F, 0.0F, -2000.0F);

        WebGpuRenderer.RenderLoadingFrame(DrawMojangLogo);
        return;

        void DrawMojangLogo()
        {
            var tessellator = Tessellator.instance;
            RenderSystem.LightingEnabled = false;
            RenderSystem.FogEnabled = false;

            // Solid white backdrop, filling the ortho space set up above (scaled coordinates, not
            // raw display pixels — the old version quaded 0..DisplayWidth/Height, which is a
            // different, usually larger, space than what the projection here maps to the window).
            RenderSystem.TextureEnabled = false;
            RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);
            tessellator.startDrawingQuads();
            tessellator.setColorOpaque_I(0xFFFFFF);
            tessellator.addVertex(0.0D, scaledResolution.ScaledHeight, 0.0D);
            tessellator.addVertex(scaledResolution.ScaledWidth, scaledResolution.ScaledHeight, 0.0D);
            tessellator.addVertex(scaledResolution.ScaledWidth, 0.0D, 0.0D);
            tessellator.addVertex(0.0D, 0.0D, 0.0D);
            tessellator.draw(ProgramSlot.Basic);

            RenderSystem.TextureEnabled = true;
            TextureManager.BindTexture(TextureManager.GetTextureId("/title/mojang.png"));
            short logoWidth = 256;
            short logoHeight = 256;
            RenderSystem.Color = new Vector4D<float>(1.0F, 1.0F, 1.0F, 1.0F);
            tessellator.setColorOpaque_I(0xFFFFFF);
            DrawTextureRegion((scaledResolution.ScaledWidth - logoWidth) / 2, (scaledResolution.ScaledHeight - logoHeight) / 2, 0, 0, logoWidth, logoHeight);
            RenderSystem.LightingEnabled = false;
            RenderSystem.FogEnabled = false;
            RenderSystem.AlphaTestEnabled = true;
            RenderSystem.AlphaThreshold = 0.1F;
        }
    }

    private static void DrawTextureRegion(int x, int y, int texX, int texY, int width, int height)
    {
        const float uScale = 1 / 256f;
        const float vScale = 1 / 256f;

        var tess = Tessellator.instance;
        tess.startDrawingQuads();
        tess.addVertexWithUV(x + 0, y + height, 0, (texX + 0) * uScale, (texY + height) * vScale);
        tess.addVertexWithUV(x + width, y + height, 0, (texX + width) * uScale, (texY + height) * vScale);
        tess.addVertexWithUV(x + width, y + 0, 0, (texX + width) * uScale, (texY + 0) * vScale);
        tess.addVertexWithUV(x + 0, y + 0, 0, (texX + 0) * uScale, (texY + 0) * vScale);
        tess.draw(ProgramSlot.Gui);
    }

    #endregion

    #region OS Interop

    [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static partial uint TimeBeginPeriod(uint period);

    [LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static partial uint TimeEndPeriod(uint period);

    public void InitializeTimer()
    {
        if (s_isWindows) TimeBeginPeriod(1);
    }

    public void CleanupTimer()
    {
        if (s_isWindows) TimeEndPeriod(1);
    }

    #endregion

    #region Application Entry Point

    public static void Startup(string[] args)
    {
        var options = ClientLaunchOptions.Parse(args);

        var content = Bootstrap.Initialize();
        StartMainThread(options, content);
    }

    private static void StartMainThread(ClientLaunchOptions options, ContentRuntime content)
    {
        Thread.CurrentThread.Name = "OmniBlock Main Thread";

        OmniBlock game = new(850, 480, false, options, content)
        {
            ForceDebugOnStart = options.Debug
        };
        game.Session = new Session(options.Username, options.SessionToken);

        if (options.SessionToken == "-")
        {
            HasPaidCheckTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        game.Run();
    }

    #endregion
}
