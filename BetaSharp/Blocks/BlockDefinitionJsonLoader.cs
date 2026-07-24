using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using BetaSharp.Registries;
using BetaSharp.Registries.Data;

namespace BetaSharp.Blocks;

internal sealed class BlockDefinitionJsonLoader(string path, LoadLocations locations) : DataAssetLoader(locations), IReadableRegistry<BlockDefinition>
{
    private const string DefaultsFileName = "_defaults.json";
    private static readonly JsonSerializerOptions s_options = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Dictionary<ResourceLocation, BlockDefinition> _byLocation = [];
    private readonly Dictionary<int, BlockDefinition> _byId = [];
    private JsonElement? _defaults;

    private protected override void Clear()
    {
        _byLocation.Clear();
        _byId.Clear();
        _defaults = null;
    }

    private protected override void OnLoadAssets(string assetPath, bool namespaced, LoadLocations location)
    {
        if (namespaced) LoadAssetsFromFolders(assetPath, location);
        else LoadAssets(Namespace.BetaSharp, assetPath, location);
    }

    private void LoadAssetsFromFolders(string assetPath, LoadLocations location)
    {
        foreach (string dir in Directory.GetDirectories(assetPath, "*", SearchOption.TopDirectoryOnly))
        {
            string dirName = Path.GetFileName(dir);
            LoadAssets(Namespace.Get(dirName), dir, location);
        }
    }

    private void LoadAssets(Namespace @namespace, string basePath, LoadLocations location)
    {
        string dir = Path.Join(basePath, path);
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

                BlockDefinition? definition = merged.Deserialize<BlockDefinition>(s_options);
                if (definition is null)
                {
                    HasErrors = true;
                    FirstErrorMessage ??= $"Failed to parse block definition from '{file}'.";
                    continue;
                }

                if (definition.ProtocolId is < 0 or > 255)
                {
                    HasErrors = true;
                    FirstErrorMessage ??= $"Block '{file}' has ProtocolId {definition.ProtocolId}, outside the valid 0-255 range.";
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(file);
                var key = new ResourceLocation(@namespace, name);
                definition.Name = key.Path;
                definition.Namespace = key.Namespace;

                if (_byLocation.TryGetValue(key, out BlockDefinition? existing))
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

    internal override BlockDefinitionJsonLoader? CloneForWorldDatapacks(string worldDatapackPath)
    {
        if (!Locations.HasFlag(LoadLocations.WorldDatapack)) return null;

        var clone = new BlockDefinitionJsonLoader(path, Locations);
        foreach (KeyValuePair<ResourceLocation, BlockDefinition> pair in _byLocation)
        {
            clone._byLocation[pair.Key] = pair.Value;
        }
        foreach (KeyValuePair<int, BlockDefinition> pair in _byId)
        {
            clone._byId[pair.Key] = pair.Value;
        }
        clone._defaults = _defaults;
        clone.LoadPacksFrom(worldDatapackPath, LoadLocations.WorldDatapack);
        return clone;
    }

    public ResourceLocation RegistryKey => new(Namespace.BetaSharp, path);

    public Holder<BlockDefinition>? Get(ResourceLocation key) =>
        _byLocation.TryGetValue(key, out BlockDefinition? value) ? new Holder<BlockDefinition>(value) : null;

    public BlockDefinition? Get(int id) => _byId.TryGetValue(id, out BlockDefinition? value) ? value : null;

    public bool ContainsId(int id) => _byId.ContainsKey(id);

    public int GetId(BlockDefinition value) => _byId.TryGetValue(value.ProtocolId, out BlockDefinition? existing) && ReferenceEquals(existing, value) ? value.ProtocolId : -1;

    public ResourceLocation? GetKey(BlockDefinition value)
    {
        foreach (KeyValuePair<ResourceLocation, BlockDefinition> pair in _byLocation)
        {
            if (ReferenceEquals(pair.Value, value)) return pair.Key;
        }

        return null;
    }

    public bool ContainsKey(ResourceLocation key) => _byLocation.ContainsKey(key);

    public IEnumerable<ResourceLocation> Keys => _byLocation.Keys;

    public IEnumerator<BlockDefinition> GetEnumerator() => _byLocation.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
