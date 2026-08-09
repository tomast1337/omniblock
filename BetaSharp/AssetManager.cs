using System.IO.Compression;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace BetaSharp;

public class AssetManager
{
    public enum AssetType
    {
        Binary,
        Text
    }

    public enum AssetProfile
    {
        Full,
        Headless
    }

    public class Asset
    {
        private readonly AssetType _type;
        private readonly byte[]? _binaryContent;
        private readonly string? _textContent;

        public Asset(byte[] binary)
        {
            _type = AssetType.Binary;
            _binaryContent = binary;
        }

        public Asset(string text)
        {
            _type = AssetType.Text;
            _textContent = text;
        }

        public AssetType GetAssetType() => _type;

        public byte[] GetBinaryContent()
        {
            if (_binaryContent == null || _type != AssetType.Binary)
            {
                throw new Exception("Attempted to get binary content from a non binary asset");
            }

            return _binaryContent;
        }

        public string GetTextContent()
        {
            if (_textContent == null || _type != AssetType.Text)
            {
                throw new Exception("Attempted to get text content from a non text asset");
            }

            return _textContent;
        }
    }

    private static readonly object s_instanceLock = new();
    private static AssetManager? s_instance;
    private static AssetProfile? s_configuredProfile;

    public static AssetManager Instance => s_instance ?? throw new InvalidOperationException("AssetManager was not initialized.");

    public static void Initialize(AssetProfile profile)
    {
        lock (s_instanceLock)
        {
            if (s_instance != null)
            {
                if (s_instance._assetProfile != profile)
                {
                    throw new InvalidOperationException($"AssetManager already initialized with profile {s_instance._assetProfile}, cannot reinitialize with {profile}.");
                }

                return;
            }

            s_configuredProfile = profile;
            s_instance = new AssetManager(profile);
        }
    }

    private readonly Dictionary<string, AssetType> _assetsToLoad = [];
    private readonly Dictionary<string, Asset> _loadedAssets = [];
    private readonly HashSet<string> _assetDirectories = [];
    private int _embeddedAssetsLoaded;
    private readonly AssetProfile _assetProfile;
    private readonly ILogger<AssetManager> _logger = Log.Instance.For<AssetManager>();

    private AssetManager(AssetProfile assetProfile)
    {
        _assetProfile = assetProfile;

        DefineHeadlessAssets();

        if (_assetProfile == AssetProfile.Full)
        {
            DefineFullAssets();
        }

        LoadLanguages();

        _logger.LogInformation($"Asset profile: {_assetProfile}. Registered {_assetsToLoad.Count} assets.");

        ExtractNeccessaryAssets();
        LoadAssets();

        _logger.LogInformation($"Loaded {_embeddedAssetsLoaded} embedded assets");
    }

    private void LoadLanguages()
    {
        string langPath = Path.Combine("assets", "lang");

        try
        {
            if (Directory.Exists(langPath))
            {
                var langFiles = Directory.EnumerateFiles(langPath, "*.json");

                foreach (string file in langFiles)
                {
                    if (file == "assets/lang/lang.json")
                    {
                        continue;
                    }

                    string fileName = Path.GetFileName(file);

                    DefineAsset("lang/" + fileName, AssetType.Text);
                }
            }
            else
            {
                Console.WriteLine($"No languages folder!");
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Unable to access files: {ex.Message}");
        }
    }

    private void DefineHeadlessAssets()
    {
        DefineAsset("achievement/map.txt", AssetType.Text);
    }

    private void DefineFullAssets()
    {
        DefineAsset("title/splashes.txt", AssetType.Text);
        DefineAsset("title/black.png", AssetType.Binary);
        DefineAsset("title/mclogo.png", AssetType.Binary);
        DefineAsset("title/mojang.png", AssetType.Binary);
        DefineAsset("achievement/bg.png", AssetType.Binary);
        DefineAsset("achievement/icons.png", AssetType.Binary);

        DefineAsset("armor/chain_1.png", AssetType.Binary);
        DefineAsset("armor/chain_2.png", AssetType.Binary);
        DefineAsset("armor/cloth_1.png", AssetType.Binary);
        DefineAsset("armor/cloth_2.png", AssetType.Binary);
        DefineAsset("armor/diamond_1.png", AssetType.Binary);
        DefineAsset("armor/diamond_2.png", AssetType.Binary);
        DefineAsset("armor/gold_1.png", AssetType.Binary);
        DefineAsset("armor/gold_2.png", AssetType.Binary);
        DefineAsset("armor/iron_1.png", AssetType.Binary);
        DefineAsset("armor/iron_2.png", AssetType.Binary);
        DefineAsset("armor/power.png", AssetType.Binary);

        DefineAsset("art/kz.png", AssetType.Binary);

        DefineAsset("environment/clouds.png", AssetType.Binary);
        DefineAsset("environment/rain.png", AssetType.Binary);
        DefineAsset("environment/snow.png", AssetType.Binary);

        DefineAsset("font/default.png", AssetType.Binary);

        DefineAsset("gui/background.png", AssetType.Binary);
        DefineAsset("gui/container.png", AssetType.Binary);
        DefineAsset("gui/crafting.png", AssetType.Binary);
        DefineAsset("gui/furnace.png", AssetType.Binary);
        DefineAsset("gui/gui.png", AssetType.Binary);
        DefineAsset("gui/icons.png", AssetType.Binary);
        DefineAsset("gui/inventory.png", AssetType.Binary);
        DefineAsset("gui/items.png", AssetType.Binary);
        DefineAsset("gui/logo.png", AssetType.Binary);
        DefineAsset("gui/particles.png", AssetType.Binary);
        DefineAsset("gui/slot.png", AssetType.Binary);
        DefineAsset("gui/trap.png", AssetType.Binary);
        DefineAsset("gui/unknown_pack.png", AssetType.Binary);
        DefineAsset("gui/Pointer.png", AssetType.Binary);
        DefineAsset("gui/Globe.png", AssetType.Binary);

        DefineAsset("gui/Logo.png", AssetType.Binary);

        string[] controllerIcons = [
            "back_button", "back_button_pressed", "down_button", "down_button_pressed",
            "dpad_down", "dpad_down_pressed", "dpad_left", "dpad_left_pressed",
            "dpad_right", "dpad_right_pressed", "dpad_up", "dpad_up_pressed",
            "guide_button", "left_bumper", "left_bumper_pressed", "left_button",
            "left_button_pressed", "left_stick", "left_stick_button",
            "left_stick_button_pressed", "left_stick_pressed_left",
            "left_stick_pressed_right", "left_trigger", "left_trigger_pressed",
            "right_bumper", "right_bumper_pressed", "right_button",
            "right_button_pressed", "right_stick", "right_stick_button",
            "right_stick_button_pressed", "right_stick_pressed_left",
            "right_stick_pressed_right", "right_trigger", "right_trigger_pressed",
            "start_button", "start_button_pressed", "unknown", "up_button",
            "up_button_pressed"
        ];

        foreach (string platform in ControllerType.ControllerTypes.Select(x => x.Key))
        {
            foreach (string icon in controllerIcons)
            {
                DefineAsset($"gui/controls/{platform}/{icon}.png", AssetType.Binary);
            }

            if (platform != "ps4" && platform != "ps5")
            {
                continue;
            }

            DefineAsset($"gui/controls/{platform}/touchpad.png", AssetType.Binary);
            DefineAsset($"gui/controls/{platform}/touchpad_pressed.png", AssetType.Binary);
        }

        DefineAsset("gui/world_types/default.png", AssetType.Binary);
        DefineAsset("gui/world_types/flat.png", AssetType.Binary);
        DefineAsset("gui/world_types/sky.png", AssetType.Binary);

        DefineAsset("item/arrows.png", AssetType.Binary);
        DefineAsset("item/boat.png", AssetType.Binary);
        DefineAsset("item/cart.png", AssetType.Binary);
        DefineAsset("item/door.png", AssetType.Binary);
        DefineAsset("item/sign.png", AssetType.Binary);

        DefineAsset("misc/dial.png", AssetType.Binary);
        DefineAsset("misc/foliagecolor.png", AssetType.Binary);
        DefineAsset("misc/footprint.png", AssetType.Binary);
        DefineAsset("misc/grasscolor.png", AssetType.Binary);
        DefineAsset("misc/mapbg.png", AssetType.Binary);
        DefineAsset("misc/mapicons.png", AssetType.Binary);
        DefineAsset("misc/pumpkinblur.png", AssetType.Binary);
        DefineAsset("misc/shadow.png", AssetType.Binary);
        DefineAsset("misc/vignette.png", AssetType.Binary);
        DefineAsset("misc/water.png", AssetType.Binary);
        DefineAsset("misc/watercolor.png", AssetType.Binary);

        DefineAsset("mob/char.png", AssetType.Binary);
        DefineAsset("mob/chicken.png", AssetType.Binary);
        DefineAsset("mob/cow.png", AssetType.Binary);
        DefineAsset("mob/creeper.png", AssetType.Binary);
        DefineAsset("mob/ghast.png", AssetType.Binary);
        DefineAsset("mob/ghast_fire.png", AssetType.Binary);
        DefineAsset("mob/pig.png", AssetType.Binary);
        DefineAsset("mob/pigman.png", AssetType.Binary);
        DefineAsset("mob/pigzombie.png", AssetType.Binary);
        DefineAsset("mob/saddle.png", AssetType.Binary);
        DefineAsset("mob/sheep.png", AssetType.Binary);
        DefineAsset("mob/sheep_fur.png", AssetType.Binary);
        DefineAsset("mob/silverfish.png", AssetType.Binary);
        DefineAsset("mob/skeleton.png", AssetType.Binary);
        DefineAsset("mob/slime.png", AssetType.Binary);
        DefineAsset("mob/spider.png", AssetType.Binary);
        DefineAsset("mob/spider_eyes.png", AssetType.Binary);
        DefineAsset("mob/squid.png", AssetType.Binary);
        DefineAsset("mob/wolf.png", AssetType.Binary);
        DefineAsset("mob/wolf_angry.png", AssetType.Binary);
        DefineAsset("mob/wolf_tame.png", AssetType.Binary);
        DefineAsset("mob/zombie.png", AssetType.Binary);

        DefineAsset("terrain/moon.png", AssetType.Binary);
        DefineAsset("terrain/sun.png", AssetType.Binary);

        DefineAsset("pack.png", AssetType.Binary);
        DefineAsset("pack.txt", AssetType.Text);

        DefineAsset("particles.png", AssetType.Binary);

        DefineAsset("terrain.png", AssetType.Binary);

        DefineEmbeddedAsset("shaders/blur.frag", AssetType.Text);
        DefineEmbeddedAsset("shaders/chunk.vert", AssetType.Text);
        DefineEmbeddedAsset("shaders/chunk.frag", AssetType.Text);
        DefineEmbeddedAsset("shaders/cloud.vert", AssetType.Text);
        DefineEmbeddedAsset("shaders/cloud.frag", AssetType.Text);
        DefineEmbeddedAsset("shaders/entity_batch.vert", AssetType.Text);
        DefineEmbeddedAsset("shaders/entity_batch.frag", AssetType.Text);
        DefineEmbeddedAsset("shaders/entity_instanced.vert", AssetType.Text);
        DefineEmbeddedAsset("shaders/entity_instanced.frag", AssetType.Text);
        DefineEmbeddedAsset("shaders/entity_textures.properties", AssetType.Text);
        DefineEmbeddedAsset("shaders/entity_parts.properties", AssetType.Text);
        DefineEmbeddedAsset("shaders/gamma.frag", AssetType.Text);
        DefineEmbeddedAsset("shaders/gbuffers_basic.vert", AssetType.Text);
        DefineEmbeddedAsset("shaders/gbuffers_basic.frag", AssetType.Text);
        DefineEmbeddedAsset("shaders/gbuffers_textured.vert", AssetType.Text);
        DefineEmbeddedAsset("shaders/gbuffers_textured.frag", AssetType.Text);
        DefineEmbeddedAsset("shaders/gbuffers_textured_lit.vert", AssetType.Text);
        DefineEmbeddedAsset("shaders/gbuffers_textured_lit.frag", AssetType.Text);
        DefineEmbeddedAsset("shaders/gbuffers_basic.wgsl", AssetType.Text);
        DefineEmbeddedAsset("shaders/gbuffers_textured.wgsl", AssetType.Text);
        DefineEmbeddedAsset("shaders/gbuffers_textured_lit.wgsl", AssetType.Text);
        DefineEmbeddedAsset("shaders/blit.wgsl", AssetType.Text);
        DefineEmbeddedAsset("shaders/chunk.wgsl", AssetType.Text);
        DefineEmbeddedAsset("shaders/sky.wgsl", AssetType.Text);
        DefineEmbeddedAsset("shaders/cloud.wgsl", AssetType.Text);
        DefineEmbeddedAsset("shaders/particle.wgsl", AssetType.Text);
        DefineEmbeddedAsset("shaders/entity_instanced.wgsl", AssetType.Text);
        DefineEmbeddedAsset("shaders/imgui.wgsl", AssetType.Text);
        DefineEmbeddedAsset("shaders/quad.vert", AssetType.Text);
        DefineEmbeddedAsset("shaders/sky.vert", AssetType.Text);
        DefineEmbeddedAsset("shaders/sky.frag", AssetType.Text);
        DefineEmbeddedAsset("shaders/ui.vert", AssetType.Text);
        DefineEmbeddedAsset("shaders/ui.frag", AssetType.Text);
        DefineEmbeddedAsset("shaders/ui_textures.properties", AssetType.Text);
        DefineEmbeddedAsset("textures/atlas/terrain.json", AssetType.Text);
        DefineEmbeddedAsset("textures/atlas/items.json", AssetType.Text);
        DefineAsset("lang/lang.json", AssetType.Text);
    }

    public Asset GetAsset(string assetPath)
    {
        if (assetPath.StartsWith('/')) assetPath = assetPath[1..];
        return _loadedAssets.TryGetValue(assetPath, out Asset? asset) ? asset : throw new Exception($"Unknown asset: {assetPath}");
    }

    private void ExtractNeccessaryAssets()
    {
        Directory.CreateDirectory("assets");

        using ZipArchive archive = ZipFile.OpenRead("b1.7.3.jar");
        Dictionary<string, ZipArchiveEntry> entries = [];
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            entries[entry.FullName] = entry;
        }

        foreach (string assetPath in _assetsToLoad.Keys)
        {
            string fsAssetPath = Path.Combine("assets", assetPath);
            string? directory = Path.GetDirectoryName(fsAssetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(fsAssetPath)) continue;

            if (entries.TryGetValue(assetPath, out ZipArchiveEntry? entry))
            {
                entry.ExtractToFile(fsAssetPath);
            }
            else
            {
                _logger.LogWarning($"Asset does not exist in jar: {assetPath}. Ensuring it exists locally.");
                if (!File.Exists(fsAssetPath))
                {
                    _logger.LogError($"Asset {assetPath} is missing both from jar and local assets folder!");
                }
            }
        }
    }

    private void LoadAssets()
    {
        foreach (KeyValuePair<string, AssetType> kvp in _assetsToLoad)
        {
            string assetPath = kvp.Key;
            AssetType type = kvp.Value;

            switch (type)
            {
                case AssetType.Binary:
                    try
                    {
                        _loadedAssets[assetPath] = new(File.ReadAllBytes("assets/" + assetPath));
                    }
                    catch (Exception e)
                    {
                        _logger.LogError($"Failed to load binary asset: {assetPath}, {e}");
                    }

                    break;
                case AssetType.Text:
                    try
                    {
                        _loadedAssets[assetPath] = new(File.ReadAllText("assets/" + assetPath));
                    }
                    catch (Exception e)
                    {
                        _logger.LogError($"Failed to load text asset: {assetPath}, {e}");
                    }

                    break;
            }
        }

        _logger.LogInformation($"Loaded {_assetsToLoad.Count} assets");

        _assetsToLoad.Clear();
    }

    private void DefineAsset(string assetPath, AssetType type)
    {
        _assetsToLoad[assetPath] = type;

        int idx = assetPath.IndexOf('/');
        if (idx == -1) return;

        string directory = assetPath[..idx];
        _assetDirectories.Add(directory);
    }

    private void DefineEmbeddedAsset(string embeddedAssetPath, AssetType type)
    {
        string embeddedAssetPathForPath = embeddedAssetPath.Replace('/', '.');

        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            string resourceName = $"{nameof(BetaSharp)}." + embeddedAssetPathForPath;

            using Stream? stream = assembly.GetManifestResourceStream(resourceName) ?? throw new Exception("Embedded resource not found: " + resourceName);
            switch (type)
            {
                case AssetType.Text:
                    {
                        using var reader = new StreamReader(stream);
                        string text = reader.ReadToEnd();
                        _loadedAssets[embeddedAssetPath] = new(text);
                        _embeddedAssetsLoaded++;
                        break;
                    }

                case AssetType.Binary:
                    {
                        using var ms = new MemoryStream();
                        stream.CopyTo(ms);
                        _loadedAssets[embeddedAssetPath] = new(ms.ToArray());
                        _embeddedAssetsLoaded++;
                        break;
                    }
            }
        }
        catch (Exception e)
        {
            _logger.LogError($"Exception while loading embedded asset: {e}");
        }
    }
}
