using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using OmniBlock.Registries;
using OmniBlock.Registries.Data;

namespace OmniBlock.Blocks;

internal sealed class BlockDefinitionJsonLoader(string path, LoadLocations locations) : DataAssetLoader(locations), IReadableRegistry<BlockDefinition>
{
    private const string DefaultsFileName = "_defaults.json";

    private static readonly JsonSerializerOptions s_options = new()
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly Dictionary<int, BlockDefinition> _byId = [];

    private readonly Dictionary<ResourceLocation, BlockDefinition> _byLocation = [];
    private JsonElement? _defaults;

    public ResourceLocation RegistryKey => new(Namespace.OmniBlock, path);

    public Holder<BlockDefinition>? Get(ResourceLocation key) => _byLocation.TryGetValue(key, out var value) ? new Holder<BlockDefinition>(value) : null;

    public BlockDefinition? Get(int id) => _byId.TryGetValue(id, out var value) ? value : null;

    public int GetId(BlockDefinition value) => value.ProtocolId >= 0 && _byId.TryGetValue(value.ProtocolId, out var existing) && ReferenceEquals(existing, value) ? value.ProtocolId : -1;

    public ResourceLocation? GetKey(BlockDefinition value)
    {
        foreach (var pair in _byLocation)
        {
            if (ReferenceEquals(pair.Value, value))
                return pair.Key;
        }

        return null;
    }

    public bool ContainsKey(ResourceLocation key) => _byLocation.ContainsKey(key);

    public IEnumerable<ResourceLocation> Keys => _byLocation.Keys;

    public IEnumerator<BlockDefinition> GetEnumerator() => _byLocation.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private protected override void Clear()
    {
        _byLocation.Clear();
        _byId.Clear();
        _defaults = null;
    }

    private protected override void OnLoadAssets(string assetPath, bool namespaced, LoadLocations location)
    {
        if (namespaced) LoadAssetsFromFolders(assetPath, location);
        else LoadAssets(Namespace.OmniBlock, assetPath, location);
    }

    private void LoadAssetsFromFolders(string assetPath, LoadLocations location)
    {
        foreach (var dir in Directory.GetDirectories(assetPath, "*", SearchOption.TopDirectoryOnly))
        {
            var dirName = Path.GetFileName(dir);
            LoadAssets(Namespace.Get(dirName), dir, location);
        }
    }

    private void LoadAssets(Namespace @namespace, string basePath, LoadLocations location)
    {
        var dir = Path.Join(basePath, path);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            return;
        }

        var defaultsPath = Path.Combine(dir, DefaultsFileName);
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

        foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            if (Path.GetFileName(file) == DefaultsFileName) continue;

            try
            {
                var raw = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(file), s_options);
                var merged = _defaults is { } d ? JsonMerge.Merge(d, raw, s_options) : raw;

                var definition = merged.Deserialize<BlockDefinition>(s_options);
                if (definition is null)
                {
                    HasErrors = true;
                    FirstErrorMessage ??= $"Failed to parse block definition from '{file}'.";
                    continue;
                }

                if (definition.ProtocolId < -1 || definition.ProtocolId >= RuntimeBlockRegistry.ProtocolIdCapacity)
                {
                    HasErrors = true;
                    FirstErrorMessage ??= $"Block '{file}' has ProtocolId {definition.ProtocolId}, outside the valid automatic-or-0-{RuntimeBlockRegistry.ProtocolIdCapacity - 1} range.";
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(file);
                ResourceLocation key = new(@namespace, name);
                definition.Name = key.Path;
                definition.Namespace = key.Namespace;

                if (_byLocation.TryGetValue(key, out var existing))
                {
                    if (existing.ProtocolId >= 0)
                        _byId.Remove(existing.ProtocolId);
                }

                _byLocation[key] = definition;
                if (definition.ProtocolId >= 0)
                {
                    if (_byId.TryGetValue(definition.ProtocolId, out var collision))
                    {
                        HasErrors = true;
                        FirstErrorMessage ??= $"Blocks '{collision.Namespace}:{collision.Name}' and '{key}' both declare ProtocolId {definition.ProtocolId}.";
                        continue;
                    }

                    _byId[definition.ProtocolId] = definition;
                }
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

        BlockDefinitionJsonLoader clone = new(path, Locations);
        foreach (var pair in _byLocation) clone._byLocation[pair.Key] = pair.Value;
        foreach (var pair in _byId) clone._byId[pair.Key] = pair.Value;
        clone._defaults = _defaults;
        clone.LoadPacksFrom(worldDatapackPath, LoadLocations.WorldDatapack);
        return clone;
    }

    public bool ContainsId(int id) => _byId.ContainsKey(id);
}
