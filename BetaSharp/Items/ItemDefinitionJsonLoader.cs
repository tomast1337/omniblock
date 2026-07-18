using System.Collections;
using System.Text.Json;
using BetaSharp.Registries;
using BetaSharp.Registries.Data;

namespace BetaSharp.Items;

internal sealed class ItemDefinitionJsonLoader : DataAssetLoader, IReadableRegistry<ItemDefinition>
{
    private const string DefaultsFileName = "_defaults.json";
    private static readonly JsonSerializerOptions s_options = new();

    private readonly string _path;
    private readonly Dictionary<ResourceLocation, ItemDefinition> _byLocation = [];
    private readonly Dictionary<int, ItemDefinition> _byId = [];
    private JsonElement? _defaults;

    public ItemDefinitionJsonLoader(string path, LoadLocations locations) : base(locations)
    {
        _path = path;
    }

    private protected override void Clear()
    {
        _byLocation.Clear();
        _byId.Clear();
        _defaults = null;
    }

    private protected override void OnLoadAssets(string path, bool namespaced, LoadLocations location)
    {
        if (namespaced) LoadAssetsFromFolders(path, location);
        else LoadAssets(Namespace.BetaSharp, path, location);
    }

    private void LoadAssetsFromFolders(string path, LoadLocations location)
    {
        foreach (string dir in Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly))
        {
            string dirName = Path.GetFileName(dir);
            LoadAssets(Namespace.Get(dirName), dir, location);
        }
    }

    private void LoadAssets(Namespace @namespace, string basePath, LoadLocations location)
    {
        string dir = Path.Join(basePath, _path);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            return;
        }

        string defaultsPath = Path.Combine(dir, DefaultsFileName);
        if (File.Exists(defaultsPath))
        {
            try
            {
                _defaults = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(defaultsPath), s_options);
            }
            catch (JsonException ex)
            {
                HasErrors = true;
                FirstErrorMessage ??= $"Syntax error in '_defaults.json' at line {ex.LineNumber}, pos {ex.BytePositionInLine}: {ex.Message}";
                return;
            }
        }

        foreach (string file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            if (Path.GetFileName(file) == DefaultsFileName) continue;

            try
            {
                JsonElement raw = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(file), s_options);
                JsonElement merged = _defaults is { } d ? JsonMerge.Merge(d, raw, s_options) : raw;

                ItemDefinition? definition = merged.Deserialize<ItemDefinition>(s_options);
                if (definition is null)
                {
                    HasErrors = true;
                    FirstErrorMessage ??= $"Failed to parse item definition from '{file}'.";
                    continue;
                }

                if (definition.ProtocolId is < 256 or >= 32000)
                {
                    HasErrors = true;
                    FirstErrorMessage ??= $"Item '{file}' has ProtocolId {definition.ProtocolId}, outside the valid 256-31999 range.";
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(file);
                var key = new ResourceLocation(@namespace, name);
                definition.Name = key.Path;
                definition.Namespace = key.Namespace;

                if (_byLocation.TryGetValue(key, out ItemDefinition? existing))
                {
                    _byId.Remove(existing.ProtocolId);
                }

                _byLocation[key] = definition;
                _byId[definition.ProtocolId] = definition;
            }
            catch (JsonException ex)
            {
                HasErrors = true;
                FirstErrorMessage ??= $"Syntax error in '{file}' at line {ex.LineNumber}, pos {ex.BytePositionInLine}: {ex.Message}";
            }
        }
    }

    internal override ItemDefinitionJsonLoader? CloneForWorldDatapacks(string worldDatapackPath)
    {
        if (!Locations.HasFlag(LoadLocations.WorldDatapack)) return null;

        var clone = new ItemDefinitionJsonLoader(_path, Locations);
        foreach (KeyValuePair<ResourceLocation, ItemDefinition> pair in _byLocation)
        {
            clone._byLocation[pair.Key] = pair.Value;
        }
        foreach (KeyValuePair<int, ItemDefinition> pair in _byId)
        {
            clone._byId[pair.Key] = pair.Value;
        }
        clone._defaults = _defaults;
        clone.LoadPacksFrom(worldDatapackPath, LoadLocations.WorldDatapack);
        return clone;
    }

    public ResourceLocation RegistryKey => new(Namespace.BetaSharp, _path);

    public Holder<ItemDefinition>? Get(ResourceLocation key) =>
        _byLocation.TryGetValue(key, out ItemDefinition? value) ? new Holder<ItemDefinition>(value) : null;

    public ItemDefinition? Get(int id) => _byId.TryGetValue(id, out ItemDefinition? value) ? value : null;

    public bool ContainsId(int id) => _byId.ContainsKey(id);

    public int GetId(ItemDefinition value) => _byId.TryGetValue(value.ProtocolId, out ItemDefinition? existing) && ReferenceEquals(existing, value) ? value.ProtocolId : -1;

    public ResourceLocation? GetKey(ItemDefinition value)
    {
        foreach (KeyValuePair<ResourceLocation, ItemDefinition> pair in _byLocation)
        {
            if (ReferenceEquals(pair.Value, value)) return pair.Key;
        }

        return null;
    }

    public bool ContainsKey(ResourceLocation key) => _byLocation.ContainsKey(key);

    public IEnumerable<ResourceLocation> Keys => _byLocation.Keys;

    public IEnumerator<ItemDefinition> GetEnumerator() => _byLocation.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
