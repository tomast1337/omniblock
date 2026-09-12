using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using OmniBlock.Client.Input;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Client.UI;
using OmniBlock.Luau.Host;
using Silk.NET.GLFW;
using File = System.IO.File;
using FileNotFoundException = System.IO.FileNotFoundException;

namespace OmniBlock.Client.Options;

public class GameOptions
{
    private static readonly string[] s_difficultyLabels =
    [
        "options.difficulty.peaceful",
        "options.difficulty.easy",
        "options.difficulty.normal",
        "options.difficulty.hard"
    ];

    private static readonly string[] s_guiScaleLabels =
    [
        "options.guiScale.auto",
        "options.guiScale.small",
        "options.guiScale.normal",
        "options.guiScale.large"
    ];

    private static readonly string[] s_cloudsQualityLabels =
    [
        "options.cloudsQuality.legacy",
        "options.cloudsQuality.off",
        "options.cloudsQuality.shader"
    ];

    private static readonly string[] s_anisoLabels = ["options.off", "2x", "4x", "8x", "16x"];
    private static readonly string[] s_msaaLabels = ["options.off", "2x", "4x", "8x"];

    public static float MaxAnisotropy = 1.0f;
    private readonly int _initialMsaa;
    private readonly KeyBinding[] _keyBindings;
    private readonly ILogger<GameOptions> _logger = Log.Instance.For<GameOptions>();
    private readonly string _optionsPath;


    private Dictionary<string, GameOption> _allOptions;

    protected OmniBlock _game;
    public bool AdvancedItemTooltips;
    public float AmountScrolled = 1.0F;
    public float Brightness = 0.5F;
    public CameraMode CameraMode = CameraMode.FirstPerson;
    public bool DebugCamera = false;
    public bool HideGUI = false;
    public bool InvertScrolling = false;

    public KeyBindingGroup[] KeyBindingGroups;
    public string LastServer = "";
    public bool ShowDebugInfo = false;


    public string Skin = "Default";
    public bool SmoothCamera = false;
    public float ZoomScale = 2.0F;

    public GameOptions(OmniBlock game, string gameDataDir)
    {
        _game = game;
        _optionsPath = Path.Combine(gameDataDir, "options.txt");

        InitializeOptions();

        _keyBindings =
        [
            KeyBindForward,
            KeyBindLeft,
            KeyBindBack,
            KeyBindRight,
            KeyBindJump,
            KeyBindSneak,
            KeyBindDrop,
            KeyBindInventory,
            KeyBindChat,
            KeyBindToggleFog,
            KeyBindZoom
        ];

        KeyBindingGroups =
        [
            new KeyBindingGroup(Translations.Get("options.movement.text"), [
                KeyBindForward,
                KeyBindLeft,
                KeyBindBack,
                KeyBindRight,
                KeyBindJump,
                KeyBindSneak
            ]),

            new KeyBindingGroup(Translations.Get("options.view.text"), [
                KeyBindInventory,
                KeyBindChat,
                KeyBindToggleFog,
                KeyBindZoom
            ]),

            new KeyBindingGroup(Translations.Get("options.other.text"), [
                KeyBindDrop
            ])
        ];

        ControllerBindings =
        [
            new ControllerBinding("controller.jump", Translations.Get("key.jump"), GamepadButton.A),
            new ControllerBinding("controller.inventory", Translations.Get("key.inventory"), GamepadButton.Y),
            new ControllerBinding("controller.drop", Translations.Get("key.drop"), GamepadButton.B),
            new ControllerBinding("controller.hotbarLeft", Translations.Get("key.hotbarLeft"), GamepadButton.LeftBumper),
            new ControllerBinding("controller.hotbarRight", Translations.Get("key.hotbarRight"), GamepadButton.RightBumper),
            new ControllerBinding("controller.sneak", Translations.Get("key.sneak"), GamepadButton.RightStick),
            new ControllerBinding("controller.zoom", Translations.Get("key.zoom"), (GamepadButton)(-1)),
            new ControllerBinding("controller.pickBlock", Translations.Get("key.pickBlock"), GamepadButton.DPadUp),
            new ControllerBinding("controller.camera", Translations.Get("key.camera"), GamepadButton.LeftStick),
            new ControllerBinding("controller.pause", Translations.Get("key.pause"), GamepadButton.Start)
        ];

        LoadOptions();
        _initialMsaa = MSAALevel;

        if (Translations.Instance.Languages.ContainsKey(LanguageOption.Value))
        {
            Language = LanguageOption.Value;
        }
        else
        {
            Language = "en_us";
        }
    }

    public FloatOption MusicVolumeOption { get; private set; }
    public FloatOption SoundVolumeOption { get; private set; }
    public FloatOption MouseSensitivityOption { get; private set; }
    public FloatOption ControllerSensitivityOption { get; private set; }
    public CycleOption ControllerTypeOption { get; private set; }
    public FloatOption FramerateLimitOption { get; private set; }
    public FloatOption FovOption { get; private set; }
    public FloatOption GammaOption { get; private set; }
    public FloatOption ChatScaleOption { get; private set; }
    public FloatOption ChatWidthOption { get; private set; }


    public BoolOption InvertMouseOption { get; private set; }
    public BoolOption ViewBobbingOption { get; private set; }
    public BoolOption VSyncOption { get; private set; }
    public BoolOption MipmapsOption { get; private set; }
    public BoolOption ChunkFadeOption { get; private set; }
    public BoolOption AlternateBlocksOption { get; private set; }
    public BoolOption MenuMusicOption { get; private set; }


    public FloatOption RenderDistanceOption { get; private set; }
    public FloatOption SimulationDistanceOption { get; private set; }
    public CycleOption CloudsQualityOption { get; private set; }
    public BoolOption SoftCloudsOption { get; private set; }
    public CycleOption DifficultyOption { get; private set; }
    public CycleOption GuiScaleOption { get; private set; }
    public CycleOption AnisotropicOption { get; private set; }
    public CycleOption MsaaOption { get; private set; }
    public BoolOption ShowCoordinatesOption { get; private set; }
    public StringOption LanguageOption { get; private set; }
    public BoolOption UICursorsOption { get; private set; }
    public BoolOption PauseOnFocusLossOption { get; private set; }
    public BoolOption CaptureMouseOption { get; private set; }


    public GameOption[] MainScreenOptions => [FovOption, DifficultyOption];
    public GameOption[] AudioScreenOptions => [MusicVolumeOption, SoundVolumeOption, MenuMusicOption];

    public GameOption[] UIScreenOptions => [GuiScaleOption, GammaOption, ShowCoordinatesOption, UICursorsOption, PauseOnFocusLossOption, CaptureMouseOption, ChatScaleOption, ChatWidthOption];


    public float MusicVolume
    {
        get => MusicVolumeOption.Value;
        set => MusicVolumeOption.Value = value;
    }

    public float SoundVolume
    {
        get => SoundVolumeOption.Value;
        set => SoundVolumeOption.Value = value;
    }

    public string Language
    {
        get => LanguageOption.Value;
        set
        {
            LanguageOption.Value = value;
            Translations.SwitchLanguage(Language);
        }
    }

    public float MouseSensitivity => MouseSensitivityOption.Value;
    public float ControllerSensitivity => ControllerSensitivityOption.Value;
    public float LimitFramerate => FramerateLimitOption.Value;
    public int? MaxFramesPerSecond => DecodeFrameRateLimit(FramerateLimitOption.Value);
    public float Fov => FovOption.Value;
    public float Gamma => GammaOption.Value * 100f;

    public bool InvertMouse
    {
        get => InvertMouseOption.Value;
        set => InvertMouseOption.Value = value;
    }

    public int RenderDistance => 4 + (int)(RenderDistanceOption.Value * 28.0f);
    public int SimulationDistance => 2 + (int)(SimulationDistanceOption.Value * 30.0f);
    public int CloudsQuality => CloudsQualityOption.Value;
    public bool SoftClouds => SoftCloudsOption.Value;
    public bool ViewBobbing => ViewBobbingOption.Value;
    public bool VSync => VSyncOption.Value;
    public int Difficulty => DifficultyOption.Value;
    public int GuiScale => GuiScaleOption.Value;
    public int AnisotropicLevel => AnisotropicOption.Value;
    public int MSAALevel => MsaaOption.Value;
    public float ChatScale => ChatScaleOption.Value;
    public float ChatWidth => ChatWidthOption.Value;
    public bool ShowCoordinates => ShowCoordinatesOption.Value;
    public bool UseMipmaps => MipmapsOption.Value;
    public bool ChunkFade => ChunkFadeOption.Value;
    public bool UICursors => UICursorsOption.Value;
    public bool AlternateBlocksEnabled => AlternateBlocksOption.Value;
    public bool MenuMusic => MenuMusicOption.Value;
    public KeyBinding KeyBindForward { get; } = new("key.forward", Keyboard.KEY_W);
    public KeyBinding KeyBindLeft { get; } = new("key.left", Keyboard.KEY_A);
    public KeyBinding KeyBindBack { get; } = new("key.back", Keyboard.KEY_S);
    public KeyBinding KeyBindRight { get; } = new("key.right", Keyboard.KEY_D);
    public KeyBinding KeyBindJump { get; } = new("key.jump", Keyboard.KEY_SPACE);
    public KeyBinding KeyBindInventory { get; } = new("key.inventory", Keyboard.KEY_E);
    public KeyBinding KeyBindDrop { get; } = new("key.drop", Keyboard.KEY_Q);
    public KeyBinding KeyBindChat { get; } = new("key.chat", Keyboard.KEY_T);
    public KeyBinding KeyBindCommand { get; } = new("key.command", Keyboard.KEY_SLASH);
    public KeyBinding KeyBindToggleFog { get; } = new("key.fog", Keyboard.KEY_F);
    public KeyBinding KeyBindSneak { get; } = new("key.sneak", Keyboard.KEY_LSHIFT);
    public KeyBinding KeyBindZoom { get; } = new("key.zoom", Keyboard.KEY_NONE);
    public ControllerBinding[] ControllerBindings { get; }

    public ShaderOptionsRegistry ShaderOptions { get; } = new();

    /// <summary>
    ///     Raised when an option changes something the texture or chunk caches derive from.
    /// </summary>
    /// <remarks>
    ///     Given an empty handler rather than left null: these are invoked directly rather than
    ///     through <c>?.Invoke</c>, so an unsubscribed instance would throw at the point an option
    ///     is changed.
    /// </remarks>
    public event Action ReloadTextures = delegate { };

    /// <inheritdoc cref="ReloadTextures" />
    public event Action ReloadChunks = delegate { };

    /// <summary>
    ///     Builds every option and the lookup over them.
    /// </summary>
    /// <remarks>
    ///     The attribute is what lets the options stay non-nullable while being built here rather
    ///     than at their declarations, which they cannot be: several read each other, and one has a
    ///     side effect on <see cref="ControlTooltip" />. It is a claim the compiler checks inside
    ///     this method, so an option added below without being assigned fails the build rather than
    ///     turning up as a null at runtime.
    /// </remarks>
    [MemberNotNull(
        nameof(MusicVolumeOption),
        nameof(SoundVolumeOption),
        nameof(MouseSensitivityOption),
        nameof(ControllerSensitivityOption),
        nameof(ControllerTypeOption),
        nameof(FramerateLimitOption),
        nameof(FovOption),
        nameof(GammaOption),
        nameof(ChatScaleOption),
        nameof(ChatWidthOption),
        nameof(InvertMouseOption),
        nameof(ViewBobbingOption),
        nameof(VSyncOption),
        nameof(MipmapsOption),
        nameof(ChunkFadeOption),
        nameof(AlternateBlocksOption),
        nameof(MenuMusicOption),
        nameof(RenderDistanceOption),
        nameof(SimulationDistanceOption),
        nameof(CloudsQualityOption),
        nameof(SoftCloudsOption),
        nameof(DifficultyOption),
        nameof(GuiScaleOption),
        nameof(AnisotropicOption),
        nameof(MsaaOption),
        nameof(ShowCoordinatesOption),
        nameof(LanguageOption),
        nameof(UICursorsOption),
        nameof(PauseOnFocusLossOption),
        nameof(CaptureMouseOption),
        nameof(_allOptions))]
    private void InitializeOptions()
    {
        MusicVolumeOption = new FloatOption("options.music", "music", 1.0F)
        {
            Steps = 100,
            OnChanged = _ => _game?.SoundManager.OnSoundOptionsChanged()
        };
        SoundVolumeOption = new FloatOption("options.sound", "sound", 1.0F)
        {
            Steps = 100,
            OnChanged = _ => _game?.SoundManager.OnSoundOptionsChanged()
        };
        MouseSensitivityOption = new FloatOption("options.sensitivity.text", "mouseSensitivity", 0.5F)
        {
            Steps = 200,
            Formatter = v => v == 0.0F
                ? Translations.Get("options.sensitivity.min")
                : v == 1.0F
                    ? Translations.Get("options.sensitivity.max")
                    : (int)(v * 200.0F) + "%"
        };
        ControllerSensitivityOption = new FloatOption("options.sensitivity.controllerText", "controllerSensitivity", 0.5F)
        {
            Steps = 200,
            Formatter = v => (int)(v * 200.0F) + "%"
        };

        string[] _ctlTypeLabels = [.. ControllerType.ControllerTypes.Select(x => x.Label)];
        string[] _ctlTypeKeys = [.. ControllerType.ControllerTypes.Select(x => x.Key)];
        ControllerTypeOption = new CycleOption("options.controllerType", "controllerType", _ctlTypeLabels, 1)
        {
            Formatter = v => _ctlTypeLabels[v],
            OnChanged = v => ControlTooltip.ControllerType = ControllerType.ControllerTypes[v]
        };
        ControlTooltip.ControllerType = ControllerType.ControllerTypes[ControllerTypeOption.Value];

        FramerateLimitOption = new FloatOption("options.fps.maxFps", "fpsLimit", 0.42857143f)
        {
            Steps = 210,
            Formatter = v =>
            {
                var fps = DecodeFrameRateLimit(v);
                return fps == null
                    ? Translations.Get("options.fps.unlimited")
                    : fps + " " + Translations.Get("options.fps.text");
            }
        };
        FovOption = new FloatOption("options.fov", "fov", 0.44444445F)
        {
            Steps = 90,
            Formatter = v => (30 + (int)(v * 90.0f)).ToString()
        };
        ShowCoordinatesOption = new BoolOption("options.showCoordinates", "showCoordinates");
        UICursorsOption = new BoolOption("options.uiCursors", "uiCursors", true);
        PauseOnFocusLossOption = new BoolOption("options.pauseOnFocusLoss", "pauseOnFocusLoss", true);
        CaptureMouseOption = new BoolOption("options.captureMouse", "captureMouse", true)
        {
            OnChanged = _ => _game?.RefreshMouseCapture()
        };
        GammaOption = new FloatOption("options.gamma", "gamma", 0.5F)
        {
            Steps = 100,
            Formatter = v => $"{(int)(v * 100.0f)}"
        };

        InvertMouseOption = new BoolOption("options.invertMouse", "invertYMouse");
        ViewBobbingOption = new BoolOption("options.viewBobbing", "bobView", true);
        VSyncOption = new BoolOption("options.vSync", "vsync")
        {
            OnChanged = v =>
            {
                Display.setVSyncEnabled(v);
                WebGpuDevice.Current?.SetVSyncEnabled(v);
            }
        };
        MipmapsOption = new BoolOption("options.mipmaps", "useMipmaps", true)
        {
            OnChanged = _ => { ReloadTextures(); }
        };

        ChunkFadeOption = new BoolOption("options.chunkFade", "chunkFade", true);
        AlternateBlocksOption = new BoolOption("options.alternateBlocks", "alternateBlocks", true)
        {
            OnChanged = _ => ReloadChunks.Invoke()
        };
        MenuMusicOption = new BoolOption("options.menuMusic", "menuMusic", true);

        RenderDistanceOption = new FloatOption("options.renderDistance.text", "viewDistance", 0.2f)
        {
            Steps = 28,
            Formatter = v => $"{4 + (int)(v * 28.0f)} " + Translations.Get("options.renderDistance.chunks"),
            OnChanged = _ =>
            {
                if (_game?.InternalServer != null)
                {
                    _game.InternalServer.SetViewDistance(RenderDistance);
                }
            }
        };
        SimulationDistanceOption = new FloatOption(
            "options.simulationDistance.text", "simulationDistance", 7.0f / 30.0f)
        {
            Steps = 30,
            Formatter = v => $"{2 + (int)(v * 30.0f)} " + Translations.Get("options.renderDistance.chunks"),
            OnChanged = _ => _game?.InternalServer?.SetSimulationDistance(SimulationDistance)
        };
        ChatScaleOption = new FloatOption("options.chatScale.text", "chatScale", 1f / 3f)
        {
            Steps = 30,
            Formatter = f => $"{(int)(f * 150.0F + 50f)}%"
        };
        ChatWidthOption = new FloatOption("options.chatWidth.text", "chatWidth", 0.5f)
        {
            Steps = 64,
            Formatter = f => $"{(int)(f * 64 + 32f)}"
        };
        CloudsQualityOption = new CycleOption("options.cloudsQuality.text", "cloudsQuality", s_cloudsQualityLabels, 2);
        SoftCloudsOption = new BoolOption("options.softClouds.text", "softClouds", true);
        DifficultyOption = new CycleOption("options.difficulty.text", "difficulty", s_difficultyLabels, 2);
        GuiScaleOption = new CycleOption("options.guiScale.text", "guiScale", s_guiScaleLabels);
        // Bound to a local before the handler that reads it back is attached, so the closure
        // captures something already assigned rather than the property mid-construction.
        CycleOption anisotropic = new("options.anisoLevel", "anisotropicLevel", s_anisoLabels)
        {
            Formatter = v => v == 0 ? Translations.Get("options.off") : s_anisoLabels[v]
        };

        anisotropic.OnChanged = v =>
        {
            var anisoValue = v == 0 ? 0 : (int)Math.Pow(2, v);
            if (anisoValue > MaxAnisotropy)
            {
                anisotropic.Value = 0;
            }

            ReloadTextures();
        };

        AnisotropicOption = anisotropic;
        MsaaOption = new CycleOption("options.msaa", "msaaLevel", s_msaaLabels)
        {
            Formatter = v =>
            {
                var result = v == 0 ? Translations.Get("options.off") : s_msaaLabels[v];
                if (v != _initialMsaa) result += " (Reload required)";
                return result;
            }
        };
        LanguageOption = new StringOption("Language", "language", "en_us")
        {
            // Takes the new value from the callback rather than reading it back off the option,
            // which is both shorter and the only form that does not reference a property that is
            // still being assigned. Nothing invokes StringOption.OnChanged today.
            OnChanged = value => Language = value
        };

        _allOptions = [];
        foreach (var option in GetAllOptions())
        {
            _allOptions[option.SaveKey] = option;
        }
    }

    internal static int? DecodeFrameRateLimit(float normalized)
    {
        var fps = 30 + (int)(Math.Clamp(normalized, 0f, 1f) * 210.0f);
        return fps >= 240 ? null : fps;
    }

    private IEnumerable<GameOption> GetAllOptions()
    {
        yield return MusicVolumeOption;
        yield return SoundVolumeOption;
        yield return MouseSensitivityOption;
        yield return ControllerSensitivityOption;
        yield return ControllerTypeOption;
        yield return FramerateLimitOption;
        yield return FovOption;
        yield return GammaOption;
        yield return InvertMouseOption;
        yield return ViewBobbingOption;
        yield return VSyncOption;
        yield return MipmapsOption;
        yield return ChunkFadeOption;
        yield return AlternateBlocksOption;
        yield return MenuMusicOption;
        yield return RenderDistanceOption;
        yield return SimulationDistanceOption;
        yield return DifficultyOption;
        yield return CloudsQualityOption;
        yield return SoftCloudsOption;
        yield return GuiScaleOption;
        yield return ChatScaleOption;
        yield return ChatWidthOption;
        yield return AnisotropicOption;
        yield return MsaaOption;
        yield return ShowCoordinatesOption;
        yield return UICursorsOption;
        yield return PauseOnFocusLossOption;
        yield return CaptureMouseOption;
        yield return LanguageOption;
    }


    public string GetKeyBindingDescription(KeyBinding binding) => Translations.Get(binding.KeyDescription);

    public string GetOptionDisplayString(KeyBinding binding) => Keyboard.getKeyName(binding.ScanCode);

    public void SetKeyBinding(KeyBinding binding, int keyCode)
    {
        binding.ScanCode = keyCode;
        SaveOptions();
    }

    internal LuauConfigValue GetScriptConfig(string key)
    {
        if (_allOptions.TryGetValue(key, out var option))
        {
            return option switch
            {
                BoolOption value => LuauConfigValue.From(value.Value),
                FloatOption value => LuauConfigValue.From(value.Value),
                CycleOption value => LuauConfigValue.From(value.Value),
                StringOption value => LuauConfigValue.From(value.Value),
                _ => default
            };
        }

        return key switch
        {
            "skin" => LuauConfigValue.From(Skin),
            "advancedItemTooltips" => LuauConfigValue.From(AdvancedItemTooltips),
            "lastServer" => LuauConfigValue.From(LastServer),
            "cameraMode" => LuauConfigValue.From((int)CameraMode),
            _ => GetBindingConfig(key)
        };
    }

    internal bool SetScriptConfig(string key, LuauConfigValue value)
    {
        var changed = _allOptions.TryGetValue(key, out var option)
            ? SetOptionValue(option, value)
            : SetNonOptionValue(key, value);

        if (changed) SaveOptions();
        return changed;
    }

    internal IReadOnlyList<LuauConfigValue>? GetScriptConfigOptions(string key)
    {
        if (key == "language")
            return [.. Translations.Instance.Languages.Keys.Select(LuauConfigValue.From)];

        if (_allOptions.TryGetValue(key, out var option) && option is CycleOption cycle)
            return [.. Enumerable.Range(0, cycle.Length).Select(index => LuauConfigValue.From(index))];

        return null;
    }

    internal static bool SetOptionValue(GameOption option, LuauConfigValue value)
    {
        switch (option, value.Kind)
        {
            case (BoolOption target, LuauConfigValueKind.Boolean):
                target.Value = value.Boolean;
                target.OnChanged?.Invoke(target.Value);
                return true;
            case (FloatOption target, LuauConfigValueKind.Number) when double.IsFinite(value.Number):
                target.Set((float)value.Number);
                return true;
            case (CycleOption target, LuauConfigValueKind.Number)
                when double.IsInteger(value.Number) && value.Number >= 0 && value.Number < target.Length:
                target.Value = (int)value.Number;
                target.OnChanged?.Invoke(target.Value);
                return true;
            case (StringOption target, LuauConfigValueKind.String):
                target.Value = value.String!;
                target.OnChanged?.Invoke(target.Value);
                return true;
            default:
                return false;
        }
    }

    private LuauConfigValue GetBindingConfig(string key)
    {
        var keyboard = _keyBindings.FirstOrDefault(binding => binding.KeyDescription == key);
        if (keyboard != null) return LuauConfigValue.From(keyboard.ScanCode);

        var controller = ControllerBindings.FirstOrDefault(binding => binding.ActionKey == key);
        return controller == null ? default : LuauConfigValue.From((int)controller.Button);
    }

    private bool SetNonOptionValue(string key, LuauConfigValue value)
    {
        switch (key, value.Kind)
        {
            case ("skin", LuauConfigValueKind.String):
                Skin = value.String!;
                return true;
            case ("advancedItemTooltips", LuauConfigValueKind.Boolean):
                AdvancedItemTooltips = value.Boolean;
                return true;
            case ("lastServer", LuauConfigValueKind.String):
                LastServer = value.String!;
                return true;
            case ("cameraMode", LuauConfigValueKind.Number)
                when double.IsInteger(value.Number) && Enum.IsDefined((CameraMode)(int)value.Number):
                CameraMode = (CameraMode)(int)value.Number;
                return true;
        }

        if (value.Kind != LuauConfigValueKind.Number || !double.IsInteger(value.Number)) return false;
        var keyboard = _keyBindings.FirstOrDefault(binding => binding.KeyDescription == key);
        if (keyboard != null)
        {
            keyboard.ScanCode = (int)value.Number;
            return true;
        }

        var controller = ControllerBindings.FirstOrDefault(binding => binding.ActionKey == key);
        if (controller == null) return false;
        controller.Button = (GamepadButton)(int)value.Number;
        return true;
    }


    public void LoadOptions()
    {
        try
        {
            if (!File.Exists(_optionsPath)) throw new FileNotFoundException($"Options file not found at {_optionsPath}");
            using var reader = new StreamReader(_optionsPath);

            while (reader.ReadLine() is { } line)
            {
                try
                {
                    var parts = line.Split(':');
                    if (parts.Length >= 2) LoadOptionFromParts(parts);
                }
                catch (Exception)
                {
                    _logger.LogError($"Skipping bad option: {line}");
                }
            }
        }
        catch (Exception)
        {
            _logger.LogError("Failed to load options");
        }
    }

    private void LoadOptionFromParts(string[] parts)
    {
        if (parts.Length < 2) return;

        var key = parts[0];
        var value = parts[1];

        if (_allOptions.TryGetValue(key, out var option))
        {
            option.Load(value);
            return;
        }

        if (key.StartsWith("shaderOpt_", StringComparison.Ordinal))
        {
            var rest = key["shaderOpt_".Length..];
            var dot = rest.IndexOf('.');
            if (dot > 0) ShaderOptions.Load(rest[..dot], rest[(dot + 1)..], value);
            return;
        }

        switch (key)
        {
            case "skin": Skin = value; break;
            case "advancedItemTooltips": AdvancedItemTooltips = value == "true"; break;
            case "lastServer": LastServer = value; break;
            case "cameraMode": CameraMode = (CameraMode)int.Parse(value); break;
            case "thirdPersonView":
                CameraMode = value == "true" ? CameraMode.ThirdPerson : CameraMode.FirstPerson;
                break;
            default:
                if (key.StartsWith("controllerButton_"))
                {
                    var actionKey = key["controllerButton_".Length..];
                    if (ControllerBindings != null)
                    {
                        foreach (var cb in ControllerBindings)
                        {
                            if (cb.ActionKey == actionKey)
                            {
                                cb.Button = (GamepadButton)int.Parse(value);
                                break;
                            }
                        }
                    }
                }
                else if (key.StartsWith("key_"))
                {
                    var bindName = key[4..];
                    for (var i = 0; i < _keyBindings.Length; ++i)
                    {
                        if (_keyBindings[i].KeyDescription == bindName)
                        {
                            _keyBindings[i].ScanCode = int.Parse(value);
                            break;
                        }
                    }
                }

                break;
        }
    }

    public void SaveOptions()
    {
        try
        {
            using var writer = new StreamWriter(_optionsPath);

            foreach (var option in GetAllOptions())
            {
                writer.WriteLine($"{option.SaveKey}:{option.Save()}");
            }

            foreach (var (key, val) in ShaderOptions.Save())
                writer.WriteLine($"{key}:{val}");

            writer.WriteLine($"skin:{Skin}");
            writer.WriteLine($"advancedItemTooltips:{AdvancedItemTooltips.ToString().ToLower()}");
            writer.WriteLine($"lastServer:{LastServer}");
            writer.WriteLine($"cameraMode:{(int)CameraMode}");

            foreach (var bind in _keyBindings)
            {
                // Don't save default key bindings to avoid cluttering the options file
                // and to allow for future changes to default key bindings without overwriting user preferences.
                if (bind.IsDefault) continue;
                writer.WriteLine($"key_{bind.KeyDescription}:{bind.ScanCode}");
            }

            if (ControllerBindings != null)
            {
                foreach (var cb in ControllerBindings)
                {
                    writer.WriteLine($"controllerButton_{cb.ActionKey}:{(int)cb.Button}");
                }
            }

            writer.Close();
        }
        catch (Exception exception)
        {
            _logger.LogError($"Failed to save options: {exception.Message}");
        }
    }

    public void OnSoundOptionsChanged() => _game?.SoundManager.OnSoundOptionsChanged();

    // for keybindings screen
    public struct KeyBindingGroup(string title, KeyBinding[] bindings)
    {
        public string Title { get; set; } = title;
        public KeyBinding[] Bindings { get; set; } = bindings;
    }
}
