using System.Diagnostics;
using System.Numerics;
using System.Runtime;
using System.Runtime.InteropServices;
using OmniBlock.Blocks;
using OmniBlock.Client.Diagnostics;
using OmniBlock.Client.DynamicTexture;
using OmniBlock.Client.Entities;
using OmniBlock.Client.Input;
using OmniBlock.Client.Network;
using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Client.Rendering.Entities;
using OmniBlock.Client.Rendering.Items;
using OmniBlock.Client.Rendering.UI;
using OmniBlock.Client.Resource;
using OmniBlock.Client.Resource.Pack;
using OmniBlock.Client.Sound;
using OmniBlock.Client.Scripting;
using OmniBlock.Client.UI;
using OmniBlock.Client.UI.Screens;
using OmniBlock.Client.UI.Screens.InGame;
using OmniBlock.Client.UI.Screens.InGame.Containers;
using OmniBlock.Client.UI.Screens.Menu;
using OmniBlock.Client.UI.Screens.Menu.Net;
using OmniBlock.Client.Worlds;
using OmniBlock.Diagnostics;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Luau;
using OmniBlock.Luau.Host;
using OmniBlock.Profiling;
using OmniBlock.Registries;
using OmniBlock.Server.Internal;
using OmniBlock.Stats;
using OmniBlock.Util;
using OmniBlock.Util.Hit;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.ClientData.Colors;
using OmniBlock.Worlds.Colors;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Storage;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.GLFW;
using Microsoft.Extensions.Logging;
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
    /// When the debug viewport is active, the top-left pixel offset of the game viewport
    /// within the window.
    /// </summary>
    public Vector2 DebugViewportOffset { get; private set; }

    /// <summary>
    /// The top-left screen position of the game viewport in ImGui/window pixels.
    /// Zero when the debug menu is closed.
    /// </summary>
    public Vector2 DebugViewportScreenPos => _debugWindowManager?.ViewportPos ?? Vector2.Zero;

    public bool ShowChunkBorders { get; private set; }
    private bool SkipRenderWorld { get; set; }
    public string DebugText { get; private set; } = "";
    public HitResult ObjectMouseOver = new(HitResultType.Miss);

    public GameRenderer GameRenderer { get; private set; }
    private WebGpuGameRenderer _webGpuRenderer;

    /// <summary>Reaches the WebGPU renderer for <see cref="LoadingScreenRenderer" /> and <see cref="LoadScreen" />, both of which draw and present frames of their own outside the main game loop.</summary>
    internal WebGpuGameRenderer WebGpuRenderer => _webGpuRenderer;
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
    private readonly LoadingScreenRenderer _loadingScreen;
    private readonly WaterSprite _textureWaterFX = new();
    private readonly LavaSprite _textureLavaFX = new();
    private readonly DebugTelemetry _debugTelemetry = new();

    private DebugWindowManager _debugWindowManager;
    private LuauWorldService? _luauWorldService;
    private bool _luauSchedulerFailed;
    private string _gameDataDir;

    /// <summary>The directory saves, options and screenshots live under.</summary>
    public string GameDataDir => _gameDataDir;

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

    private OmniBlock(int width, int height, bool isFullscreen, ClientLaunchOptions launchOptions)
    {
        _launchOptions = launchOptions;
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

        // After SetupOpenGLAndInput, not before: that's where ImGui.CreateContext() runs, and the
        // WebGPU splash goes through the same ImGuiWgpuBackend/RenderState machinery every other
        // frame does — drawing it any earlier means drawing before that machinery exists.
        SetupOpenGLAndInput();

        LoadScreen();

        SetupResourcesAndPostProcessing();

        StatFileWriter.ReadStat(Stats.Stats.StartGameStat, 1);
        Navigate(CreateMainMenuScreen());
        SignalClientReady();
    }

    /// <summary>
    /// True after resources are loaded and the initial main menu has been initialized.
    /// </summary>
    public bool IsClientReady => _clientReady.IsReady;

    /// <summary>
    /// Fires at the post-main-menu client-ready boundary. A handler registered after the
    /// boundary has already been reached runs immediately.
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
        if (_launchOptions.StartupScript is not { } script)
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
        string scheduledSource = $"OMNI.run(function()\n{script.Source}\nend)";
        LuauState.ResetInstructionBudget(LuauInstructionBudgetPerTick);
        if (!LuauState.TryExecute(scheduledSource, out string error))
        {
            _logger.LogError("Failed to schedule startup script {Path}: {Error}", script.Path, error);
            return;
        }

        _logger.LogInformation("Scheduled startup script {Path}", script.Path);
    }

    private unsafe void SetupDisplay()
    {
        int maximumWidth = Display.getDisplayMode().getWidth();
        int maximumHeight = Display.getDisplayMode().getHeight();

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

        _gameDataDir = OmniBlockDir;
        SaveLoader = new RegionWorldStorageSource(Path.Combine(_gameDataDir, "saves"));
        Options = new GameOptions(this, _gameDataDir);
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
                Display.getFramebufferWidth(), Display.getFramebufferHeight());
            GLManager.Init();
            _webGpuRenderer = new WebGpuGameRenderer(this);
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
            LuauDomHost.Install(LuauState.Handle);
            LuauLogHost.WriteLine = message => Log.Instance.For("Luau").LogInformation("{Message}", message);
            LuauLogHost.Install(LuauState.Handle);
            LuauState.ResetInstructionBudget(LuauInstructionBudgetPerTick);
            if (!LuauState.TryExecute(LuauDomHost.Bootstrap, out string domBootstrapError))
            {
                _logger.LogError("Failed to install the Luau DOM bootstrap: {Error}", domBootstrapError);
            }

            if (!LuauState.TryExecute(LuauScheduler.Bootstrap, out string schedulerBootstrapError))
            {
                _luauSchedulerFailed = true;
                _logger.LogError("Failed to install the Luau scheduler bootstrap: {Error}", schedulerBootstrapError);
            }

            LuauConfigHost.Get = Options.GetScriptConfig;
            LuauConfigHost.Set = Options.SetScriptConfig;
            LuauConfigHost.Options = Options.GetScriptConfigOptions;
            LuauConfigHost.Install(LuauState.Handle);
            if (!LuauState.TryExecute(LuauConfigHost.Bootstrap, out string configBootstrapError))
            {
                _logger.LogError("Failed to install the Luau configuration bootstrap: {Error}", configBootstrapError);
            }

            _luauWorldService = new LuauWorldService(
                SaveLoader,
                () => World == null && InternalServer == null);
            LuauWorldsHost.List = _luauWorldService.List;
            LuauWorldsHost.Load = _luauWorldService.RequestLoad;
            LuauWorldsHost.Install(LuauState.Handle);
            if (!LuauState.TryExecute(LuauWorldsHost.Bootstrap, out string worldsBootstrapError))
            {
                _logger.LogError("Failed to install the Luau worlds bootstrap: {Error}", worldsBootstrapError);
            }

            LuauUiHost.Dispatch = UiCommandRegistry.Invoke;
            LuauUiHost.Install(LuauState.Handle);

            LuauRegistryHost.RegisterUi = (string name, out int id) =>
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

        TexturePackList = new TexturePacks(this, new DirectoryInfo(_gameDataDir));
        TextureManager = new TextureManager(this, TexturePackList, Options);
        TextRenderer = new TextRenderer(Options, TextureManager);

        TextureHandle terrainTexture = TextureManager.GetTextureId("/terrain.png");
        TextureHandle itemsTexture = TextureManager.GetTextureId("/gui/items.png");

        BuildBatchRenderer(terrainTexture.Id, itemsTexture.Id);

        UIContext = new UIContext(
            Options,
            TextRenderer,
            UiBatchRenderer,
            TextureManager,
            terrainTexture,
            itemsTexture,
            playClickSound: () => SoundManager.PlaySoundFX("random.click", 1.0f, 1.0f),
            displaySize: () => new Vector2D<int>(DisplayWidth, DisplayHeight),
            inputDisplaySize: () =>
            {
                if (!Options.ShowDebugInfo || _debugWindowManager == null)
                {
                    return new Vector2D<int>(DisplayWidth, DisplayHeight);
                }

                Vector2 vs = _debugWindowManager.ViewportSize;
                return vs is { X: > 0, Y: > 0 } ? new Vector2D<int>((int)vs.X, (int)vs.Y) : new Vector2D<int>(DisplayWidth, DisplayHeight);
            },
            controllerState: this,
            VirtualCursor,
            Timer,
            navigator: this,
            hasWorld: () => World != null,
            mouseOffset: () => new Vector2D<int>((int)DebugViewportOffset.X, (int)DebugViewportOffset.Y),
            renderTargetSize: () =>
            {
                if (_webGpuRenderer.FramebufferSize is { Width: > 0, Height: > 0 } size)
                {
                    return new Vector2D<int>((int)size.Width, (int)size.Height);
                }

                return new Vector2D<int>(Display.getFramebufferWidth(), Display.getFramebufferHeight());
            }
        );

        SkinManager = new SkinManager(TextureManager);
        WaterColors.loadColors(TextureManager.GetColors("/misc/watercolor.png"));
        GrassColors.loadColors(TextureManager.GetColors("/misc/grasscolor.png"));
        FoliageColors.loadColors(TextureManager.GetColors("/misc/foliagecolor.png"));
        GameRenderer = new GameRenderer(this);
        EntityRenderDispatcher.Instance.SkinManager = SkinManager;
        EntityRenderDispatcher.Instance.HeldItemRenderer = new HeldItemRenderer(this);
        StatFileWriter = new StatFileWriter(Session, _gameDataDir);
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

        uint fontTexId = TextRenderer.FontTextureId;
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
        TextureHandle handle = TextureManager.GetTextureId("/" + assetPath);
        UiBatchRenderer.RegisterTextureByPath(assetPath, (uint)handle.Id);
    }

    private unsafe void SetupOpenGLAndInput()
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

                Vector2 vp = _debugWindowManager.ViewportPos;
                Vector2 vs = _debugWindowManager.ViewportSize;
                return vs is { X: > 0, Y: > 0 } ? new Vector2D<int>((int)(vp.X + vs.X / 2), (int)(vp.Y + vs.Y / 2)) : new Vector2D<int>(Display.getWidth() / 2, Display.getHeight() / 2);
            }
        };

        GLManager.TextureEnabled = true;
        GLManager.ShadeModel = ShadeModel.Smooth;

        // The state every frame starts from, and the one the rest of the renderer is traced
        // against. It is named here rather than assembled from a handful of enables so that the
        // applier's cache starts out true instead of empty: depth tested and written, compared
        // Lequal, nothing blended, nothing culled. Culling being off is not an oversight — GL
        // starts with it disabled and the old code only ever set which face to cull, never turned
        // it on, which is the same thing RenderState.Entity settles on for the entity pass.
        GLManager.State.Apply(RenderState.Entity);

        GLManager.AlphaTestEnabled = true;
        GLManager.AlphaThreshold = 0.1F;
        // Both stacks to identity. The model-view holds the default from process start, but
        // stating it explicitly means a later stack-owner change doesn't silently infect this.
        GLManager.Projection.LoadIdentity();
        GLManager.ModelView.LoadIdentity();
    }

    private void SetupResourcesAndPostProcessing()
    {
        RegistryAccess = RegistryAccess.Build();

        SoundManager.LoadSoundSettings(Options);
        DefaultMusicCategories.Register(SoundManager);

        TextureManager.AddDynamicTexture(_textureLavaFX);
        TextureManager.AddDynamicTexture(_textureWaterFX);
        TextureManager.AddDynamicTexture(new NetherPortalSprite());
        TextureManager.AddDynamicTexture(new CompassSprite(this));
        TextureManager.AddDynamicTexture(new ClockSprite(this));
        TextureManager.AddDynamicTexture(new WaterSideSprite());
        TextureManager.AddDynamicTexture(new LavaSideSprite());
        TextureManager.AddDynamicTexture(new FireSprite("fire_layer_0", "custom_fire_e_w.png"));
        TextureManager.AddDynamicTexture(new FireSprite("fire_layer_1", "custom_fire_n_s.png"));

        WorldRenderer = new WorldRenderer(this, TextureManager);
        ParticleManager = new ParticleManager(World, TextureManager);

        _ = new ResourceManager()
            .Add(new BetaResourceDownloader(this, _gameDataDir))
            .Add(new ModernAssetDownloader(this, _gameDataDir,
            [
                "minecraft/sounds/music/menu/moog_city_2.ogg",
                "minecraft/sounds/music/menu/mutation.ogg",
                "minecraft/sounds/music/menu/floating_trees.ogg",
                "minecraft/sounds/music/menu/beginning_2.ogg",
            ])).LoadAllAsync();

        HUD = new HUD(UIContext, new HUDContext(
            () => Player,
            () => PlayerController,
            () => World,
            () => CurrentScreen == null && Player != null && World != null
                ? new InGameTipContext(ObjectMouseOver, World.Reader, Player.Inventory.ItemInHand)
                : null,
            () => _isMainMenuOpen
        ));

        EntityRenderDispatcher.Instance.SkinManager.RequestDownload(Session.username, true);
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

    private void Shutdown()
    {
        Running = false;
    }

    private void ShutdownGame()
    {
        try
        {
            StopInternalServer();
            StatFileWriter.Tick();
            StatFileWriter.SyncStats();

            _logger.LogInformation("Stopping!");

            try { ChangeWorld(null); } catch (Exception) { }

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
            LuauConfigHost.Get = null;
            LuauConfigHost.Set = null;
            LuauConfigHost.Options = null;
            LuauWorldsHost.List = null;
            LuauWorldsHost.Load = null;
            _luauWorldService = null;
            LuauLogHost.WriteLine = null;
            LuauState?.Dispose();
            Mouse.destroy();
            Keyboard.destroy();

            Texture2D.LogLeakReport();
        }
        finally
        {
            Display.destroy();
            CleanupTimer();

            if (!_hasCrashed)
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
            long lastFpsCheckTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int frameCounter = 0;

            while (Running)
            {
                long frameStartNano = Stopwatch.GetTimestamp();

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
                        Vector2D<int> inputSize = UIContext.InputDisplaySize;
                        VirtualCursor.Update(CurrentScreen, Options, inputSize.X, inputSize.Y, Timer.DeltaTime);
                    }

                    if (IsGamePaused && World != null)
                    {
                        float previousRenderPartialTicks = Timer.RenderPartialTicks;
                        Timer.UpdateTimer();
                        Timer.RenderPartialTicks = previousRenderPartialTicks;
                    }
                    else
                    {
                        Timer.UpdateTimer();
                    }

                    bool imguiThisFrame = Options.ShowDebugInfo;
                    if (imguiThisFrame)
                    {
                        ImGuiImplGLFW.NewFrame();

                        unsafe
                        {
                            ImGuiIO* io = ImGui.GetIO();
                            int w = Math.Max(1, Display.getWidth());
                            int h = Math.Max(1, Display.getHeight());
                            io->DisplaySize = new Vector2(w, h);
                            io->DisplayFramebufferScale = new Vector2(
                                Display.getFramebufferWidth() / (float)w,
                                Display.getFramebufferHeight() / (float)h);
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

                    long tickStartTime = Stopwatch.GetTimestamp();

                    using (Profiler.Begin("Ticks"))
                    {
                        for (int tickIndex = 0; tickIndex < Timer.ElapsedTicks; ++tickIndex)
                        {
                            ++TicksRan;
                            RunTick(Timer.RenderPartialTicks);
                        }
                    }

                    long tickElapsedTime = Stopwatch.GetTimestamp() - tickStartTime;

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

                    // WebGPU hands the offscreen framebuffer's own colour view to
                    // ImGuiWgpuBackend.RegisterExternalTexture, inside WebGpuGameRenderer.RenderFrame,
                    // once ViewportSize below tells it to.
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
                        _webGpuRenderer.ViewportSize = null;
                        DebugViewportOffset = Vector2.Zero;
                    }

                    // WebGPU builds and submits its ImGui draw data as one of the passes recorded
                    // inside RenderFrame() below, so that data has to already exist by the time
                    // RenderFrame() runs — ImGui.Render() has to come first. The ViewportTextureId
                    // this feeds ImGui.Image is therefore last frame's, same as it always was here —
                    // RenderFrame() has not run yet to produce a fresher one.
                    if (imguiThisFrame)
                    {
                        _debugWindowManager.ViewportTextureId = _webGpuRenderer.ViewportTextureId;

                        using (Profiler.Begin("ImguiBuild"))
                        {
                            _debugWindowManager.Render(Timer.DeltaTime);
                        }

                        // Read the size Render() just produced, not a value read before it ran —
                        // RenderFrame() below resizes the offscreen texture from this same reading,
                        // and the draw data ImGui.Render() is about to bake already has an Image
                        // widget sized from it. Same reading, same frame, for both: no lag left for
                        // a drag to fall behind on.
                        Vector2 vpSize = _debugWindowManager.ViewportSize;
                        if (vpSize.X > 0 && vpSize.Y > 0)
                        {
                            int vpW = (int)vpSize.X, vpH = (int)vpSize.Y;
                            _webGpuRenderer.ViewportSize = ((uint)vpW, (uint)vpH);
                            DisplayWidth = vpW;
                            DisplayHeight = vpH;

                            DebugViewportOffset = new Vector2(
                                _debugWindowManager.ViewportPos.X,
                                Display.getHeight() - vpH - _debugWindowManager.ViewportPos.Y);
                        }
                        else
                        {
                            _webGpuRenderer.ViewportSize = null;
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
                            _webGpuRenderer.ImguiOpen = imguiThisFrame;
                            _webGpuRenderer.RenderFrame(Timer.RenderPartialTicks,
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
                        Thread.Sleep(10);
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
        MetricRegistry.Set(RenderMetrics.MeshVersionAllocated, ChunkMeshVersion.TotalAllocated);
        MetricRegistry.Set(RenderMetrics.MeshVersionReleased, ChunkMeshVersion.TotalReleased);
        MetricRegistry.Set(RenderMetrics.TextureBindsLastFrame, TextureStats.BindsLastFrame);
        MetricRegistry.Set(RenderMetrics.TextureAvgBinds, (float)TextureStats.AverageBindsPerFrame);
        MetricRegistry.Set(RenderMetrics.TextureActive, Texture2D.ActiveTextureCount);
        MetricRegistry.Set(RenderMetrics.EntitiesRendered, WorldRenderer.CountEntitiesRendered);
        MetricRegistry.Set(RenderMetrics.EntitiesHidden, WorldRenderer.CountEntitiesHidden);
        MetricRegistry.Set(RenderMetrics.EntitiesTotal, WorldRenderer.CountEntitiesTotal);
        MetricRegistry.Set(RenderMetrics.ParticlesActive, ParticleManager.ActiveParticleCount);
    }

    private void ReportFrameTelemetry(long frameStartNano)
    {
        long frameEndNano = Stopwatch.GetTimestamp();
        double thisFrameTimeMs = (frameEndNano - frameStartNano) / 1000000.0;
        _debugTelemetry.RecordFrameTime(thisFrameTimeMs);
        MetricRegistry.Set(ClientMetrics.FrameTimeMs, (float)thisFrameTimeMs);

        Profiler.Record("FrameTime", thisFrameTimeMs);
        Profiler.CaptureFrame();
    }

    #endregion

    #region Tick Logic

    public void RunTick(float partialTicks)
    {
        using Profiler.ProfilerScope _tick = Profiler.Begin("Tick");

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
                    !LuauScheduler.Tick(luauState, 1.0 / Timer.TicksPerSecond, out string schedulerError))
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

        bool f11Down = Keyboard.isKeyDown(Keyboard.KEY_F11);
        if (f11Down && !_prevF11Down)
        {
            ToggleFullscreen();
        }
        _prevF11Down = f11Down;

        // F3 uses edge detection so it works even when
        // CurrentScreen.HandleInput() has already consumed all keyboard events.
        bool f3Down = Keyboard.isKeyDown(Keyboard.KEY_F3);
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
            TextureManager.BindTexture(TextureManager.GetTextureId("/terrain.png"));
            if (!IsGamePaused)
            {
                TextureManager.Tick();
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
        if (_luauWorldService?.TryTakePending(out LuauWorldLoadRequest? request) != true || request == null)
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
            long timeSinceLastMouseEvent = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _systemTime;

            if (Mouse.getEventDX() != 0 || Mouse.getEventDY() != 0)
            {
                IsControllerMode = false;
                Mouse.setCursorVisible(true);
            }

            if (timeSinceLastMouseEvent <= 200L)
            {
                int mouseWheelDelta = Mouse.getEventDWheel();
                if (mouseWheelDelta != 0)
                {
                    IsControllerMode = false;
                    Mouse.setCursorVisible(true);

                    bool zoomHeld = CurrentScreen == null && InGameHasFocus && Keyboard.isKeyDown(Options.KeyBindZoom.ScanCode);
                    if (zoomHeld)
                    {
                        int mouseWheelDirection = mouseWheelDelta > 0 ? 1 : -1;
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
                            Options.AmountScrolled += (float)mouseWheelDelta * 0.25F;
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
                        bool clickTargetsGame = !Options.ShowDebugInfo || _debugWindowManager.GameViewportFocused;
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

                for (int slotIndex = 0; slotIndex < 9; ++slotIndex)
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
            if (Mouse.isButtonDown(0) && (float)(TicksRan - MouseTicksRan) >= Timer.TicksPerSecond / 4.0F && InGameHasFocus)
            {
                ClickMouse(0);
                MouseTicksRan = TicksRan;
            }

            if (Mouse.isButtonDown(1) && (float)(TicksRan - MouseTicksRan) >= Timer.TicksPerSecond / 4.0F && InGameHasFocus)
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

            bool shouldPerformSecondaryAction = true;
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
                int blockX = ObjectMouseOver.BlockX;
                int blockY = ObjectMouseOver.BlockY;
                int blockZ = ObjectMouseOver.BlockZ;
                int blockSide = ObjectMouseOver.Side;
                if (mouseButton == 0)
                {
                    PlayerController.ClickBlock(blockX, blockY, blockZ, ObjectMouseOver.Side);
                }
                else
                {
                    ItemStack selectedItem = Player.Inventory.ItemInHand;
                    int itemCountBefore = selectedItem != null ? selectedItem.Count : 0;
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
                ItemStack selectedItem = Player.Inventory.ItemInHand;
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
            int blockId = World.Reader.GetBlockId(ObjectMouseOver.BlockX, ObjectMouseOver.BlockY, ObjectMouseOver.BlockZ);
            int blockMeta = World.Reader.GetBlockMeta(ObjectMouseOver.BlockX, ObjectMouseOver.BlockY, ObjectMouseOver.BlockZ);
            Block hitBlock = Block.Blocks[blockId];

            (int primaryMeta, int backupId, int backupMeta) = hitBlock.GetPickBlockItem(blockMeta);

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
                    int blockX = ObjectMouseOver.BlockX;
                    int blockY = ObjectMouseOver.BlockY;
                    int blockZ = ObjectMouseOver.BlockZ;
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

        bool useBedSpawn = respawnPos is not null;
        Vec3I finalRespawnPos = respawnPos ?? World.Properties.GetSpawnPos();

        World.UpdateSpawnPosition();
        World.Entities.UpdateEntityLists();

        int previousPlayerId = 0;
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
        InternalServer = new InternalServer(Path.Combine(OmniBlockDir, "saves"), worldDir, worldSettings, Options.RenderDistance, Options.Difficulty);
        InternalServer.RegistryAccess = RegistryAccess;
        InternalServer.RunThreaded("Internal Server");
    }

    private void StopInternalServer()
    {
        if (InternalServer == null)
        {
            return;
        }

        InternalServer.Stop();
        while (!InternalServer.stopped)
        {
            Thread.Sleep(1);
        }

        InternalServer = null;
    }

    private bool IsMultiplayerWorld()
    {
        return World is { IsRemote: true };
    }

    private void ShowText(string loadingText)
    {
        _loadingScreen.BeginLoading(loadingText);
        _loadingScreen.SetStage("Building terrain");
        short loadingRadius = 128;
        int loadedChunkCount = 0;
        int totalChunksToLoad = loadingRadius * 2 / 16 + 1;
        totalChunksToLoad *= totalChunksToLoad;
        Vec3I centerPos = World.Properties.GetSpawnPos();

        if (Player != null)
        {
            centerPos.X = (int)Player.X;
            centerPos.Z = (int)Player.Z;
        }

        for (int xOffset = -loadingRadius; xOffset <= loadingRadius; xOffset += 16)
        {
            for (int zOffset = -loadingRadius; zOffset <= loadingRadius; zOffset += 16)
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
        UIScreen? oldScreen = CurrentScreen;
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
                newScreen = new GameOverScreen(UIContext, Player.getScore(), Player.Respawn, canRespawn: Session != null, exitToTitle: () => ChangeWorld(null!));
            }
        }

        if (newScreen is MainMenuScreen)
        {
            HUD.Chat.ClearMessages();
        }

        if (InternalServer != null)
        {
            bool shouldPause = newScreen?.PausesGame ?? false;
            if (shouldPause || (CurrentScreen?.PausesGame ?? false))
            {
                InternalServer.Paused = shouldPause;
            }
        }

        CurrentScreen = newScreen;

        if (CurrentScreen != null)
        {
            Vector2D<int> inputSizeForReset = UIContext.InputDisplaySize;
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

        bool isMp = IsMultiplayerWorld() && InternalServer == null;
        string quitText = isMp ? Translations.Get("menu.disconnect") : Translations.Get("menu.saveAndQuitToTitle");
        int saveStep = 0;
        Navigate(new IngameMenuScreen(UIContext, StatFileWriter, SetIngameFocus, quitText, () =>
        {
            if (IsMultiplayerWorld()) World.Disconnect();
            StopInternalServer();
            ChangeWorld(null);
        }, () => World?.AttemptSaving(saveStep++) ?? false, TexturePackList));
    }

    public void SetIngameFocus()
    {
        if (!Display.isActive())
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
    private ClientNetworkContext CreateNetworkContext() => new(this, this, this, Session, StatFileWriter, ParticleManager, HUD.AddChatMessage, this, Path.Combine(_gameDataDir, "chunkcache"));

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
                DisplayMode desktopMode = Display.getDesktopDisplayMode();
                int centerX = (desktopMode.getWidth() - DisplayWidth) / 2;
                int centerY = (desktopMode.getHeight() - DisplayHeight) / 2;
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

        int framebufferWidth = Display.getFramebufferWidth();
        int framebufferHeight = Display.getFramebufferHeight();

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
                _webGpuRenderer.ScreenshotRequested = true;
            }
        }
        else
        {
            _isTakingScreenshot = false;
        }

        if (_webGpuRenderer.ScreenshotResult is { } webGpuResult)
        {
            HUD.AddChatMessage(webGpuResult);
            _webGpuRenderer.ScreenshotResult = null;
        }
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

        int slashIndex = resourcePath.IndexOf("/");
        string category = resourcePath.Substring(0, slashIndex);
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
            int subSlash = resourcePath.IndexOf("/");
            string subCategory = resourcePath.Substring(0, subSlash);
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
        GLManager.Projection.LoadIdentity();
        GLManager.Projection.Ortho(0.0D, scaledResolution.ScaledWidth, scaledResolution.ScaledHeight, 0.0D, 1000.0D, 3000.0D);
        GLManager.ModelView.LoadIdentity();
        GLManager.ModelView.Translate(0.0F, 0.0F, -2000.0F);

        _webGpuRenderer.RenderLoadingFrame(DrawMojangLogo);
        return;

        void DrawMojangLogo()
        {
            Tessellator tessellator = Tessellator.instance;
            GLManager.LightingEnabled = false;
            GLManager.FogEnabled = false;

            // Solid white backdrop, filling the ortho space set up above (scaled coordinates, not
            // raw display pixels — the old version quaded 0..DisplayWidth/Height, which is a
            // different, usually larger, space than what the projection here maps to the window).
            GLManager.TextureEnabled = false;
            GLManager.Color = new(1.0F, 1.0F, 1.0F, 1.0F);
            tessellator.startDrawingQuads();
            tessellator.setColorOpaque_I(0xFFFFFF);
            tessellator.addVertex(0.0D, scaledResolution.ScaledHeight, 0.0D);
            tessellator.addVertex(scaledResolution.ScaledWidth, scaledResolution.ScaledHeight, 0.0D);
            tessellator.addVertex(scaledResolution.ScaledWidth, 0.0D, 0.0D);
            tessellator.addVertex(0.0D, 0.0D, 0.0D);
            tessellator.draw(ProgramSlot.Basic);

            GLManager.TextureEnabled = true;
            TextureManager.BindTexture(TextureManager.GetTextureId("/title/mojang.png"));
            short logoWidth = 256;
            short logoHeight = 256;
            GLManager.Color = new(1.0F, 1.0F, 1.0F, 1.0F);
            tessellator.setColorOpaque_I(0xFFFFFF);
            DrawTextureRegion((scaledResolution.ScaledWidth - logoWidth) / 2, (scaledResolution.ScaledHeight - logoHeight) / 2, 0, 0, logoWidth, logoHeight);
            GLManager.LightingEnabled = false;
            GLManager.FogEnabled = false;
            GLManager.AlphaTestEnabled = true;
            GLManager.AlphaThreshold = 0.1F;
        }
    }

    private static void DrawTextureRegion(int x, int y, int texX, int texY, int width, int height)
    {
        const float uScale = 1 / 256f;
        const float vScale = 1 / 256f;

        Tessellator tess = Tessellator.instance;
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
        ClientLaunchOptions options = ClientLaunchOptions.Parse(args);

        Bootstrap.Initialize();
        StartMainThread(options);
    }

    private static void StartMainThread(ClientLaunchOptions options)
    {
        Thread.CurrentThread.Name = "OmniBlock Main Thread";

        OmniBlock game = new(850, 480, false, options) { ForceDebugOnStart = options.Debug };
        game.Session = new Session(options.Username, options.SessionToken);

        if (options.SessionToken == "-")
        {
            HasPaidCheckTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        game.Run();
    }

    #endregion
}
