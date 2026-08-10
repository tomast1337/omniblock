using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using OmniBlock.Registries;
using OmniBlock.Registries.Data;

namespace OmniBlock.Entities;

/// <summary>
///     Loads <see cref="EntityDefinition" />s from <c>assets/entity/*.json</c>, merged over
///     <c>_defaults.json</c>, plus datapack layers. A dedicated <see cref="DataAssetLoader" />
///     subclass, like items and blocks have: entities carry a stable explicit protocol id, and
///     <c>DataAssetLoader&lt;T&gt;</c> hardcodes <c>GetId() =&gt; -1</c>.
/// </summary>
internal sealed class EntityDefinitionJsonLoader(string path, LoadLocations locations) : DataAssetLoader(locations), IReadableRegistry<EntityDefinition>
{
    private const string DefaultsFileName = "_defaults.json";

    /// <summary>
    ///     Vanilla Beta 1.7.3 assigns 1-127. Spawn packets transmit the id as a signed byte, so a
    ///     value outside this range would be truncated into a different entity on the wire.
    /// </summary>
    private const int MinProtocolId = 1;

    private const int MaxProtocolId = sbyte.MaxValue;

    private static readonly JsonSerializerOptions s_options = new()
    {
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly Dictionary<int, EntityDefinition> _byId = [];

    private readonly Dictionary<ResourceLocation, EntityDefinition> _byLocation = [];
    private JsonElement? _defaults;

    public ResourceLocation RegistryKey => new(Namespace.OmniBlock, path);

    public Holder<EntityDefinition>? Get(ResourceLocation key) =>
        _byLocation.TryGetValue(key, out EntityDefinition? value) ? new Holder<EntityDefinition>(value) : null;

    public EntityDefinition? Get(int id) => _byId.TryGetValue(id, out EntityDefinition? value) ? value : null;

    public int GetId(EntityDefinition value) =>
        _byId.TryGetValue(value.ProtocolId, out EntityDefinition? existing) && ReferenceEquals(existing, value) ? value.ProtocolId : -1;

    public ResourceLocation? GetKey(EntityDefinition value)
    {
        foreach (KeyValuePair<ResourceLocation, EntityDefinition> pair in _byLocation)
        {
            if (ReferenceEquals(pair.Value, value))
            {
                return pair.Key;
            }
        }

        return null;
    }

    public bool ContainsKey(ResourceLocation key) => _byLocation.ContainsKey(key);

    public IEnumerable<ResourceLocation> Keys => _byLocation.Keys;

    public IEnumerator<EntityDefinition> GetEnumerator() => _byLocation.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private protected override void Clear()
    {
        _byLocation.Clear();
        _byId.Clear();
        _defaults = null;
    }

    private protected override void OnLoadAssets(string assetPath, bool namespaced, LoadLocations location)
    {
        if (namespaced)
        {
            LoadAssetsFromFolders(assetPath, location);
        }
        else
        {
            LoadAssets(Namespace.OmniBlock, assetPath, location);
        }
    }

    private void LoadAssetsFromFolders(string assetPath, LoadLocations location)
    {
        foreach (string dir in Directory.GetDirectories(assetPath, "*", SearchOption.TopDirectoryOnly))
        {
            LoadAssets(Namespace.Get(Path.GetFileName(dir)), dir, location);
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
            if (Path.GetFileName(file) == DefaultsFileName)
            {
                continue;
            }

            try
            {
                JsonElement raw = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(file), s_options);
                JsonElement merged = _defaults is { } d ? JsonMerge.Merge(d, raw, s_options) : raw;

                EntityDefinition? definition = merged.Deserialize<EntityDefinition>(s_options);
                if (definition is null)
                {
                    HasErrors = true;
                    FirstErrorMessage ??= $"Failed to parse entity definition from '{file}'.";
                    continue;
                }

                if (definition.ProtocolId is < MinProtocolId or > MaxProtocolId)
                {
                    HasErrors = true;
                    FirstErrorMessage ??= definition.ProtocolId < 0
                        ? $"Entity '{file}' is missing a ProtocolId."
                        : $"Entity '{file}' has ProtocolId {definition.ProtocolId}, outside the valid {MinProtocolId}-{MaxProtocolId} range (spawn packets transmit it as a signed byte).";
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(file);
                ResourceLocation key = new(@namespace, name);
                definition.Name = key.Path;
                definition.Namespace = key.Namespace;

                if (_byLocation.TryGetValue(key, out EntityDefinition? existing))
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

    internal override EntityDefinitionJsonLoader? CloneForWorldDatapacks(string worldDatapackPath)
    {
        if (!Locations.HasFlag(LoadLocations.WorldDatapack))
        {
            return null;
        }

        EntityDefinitionJsonLoader clone = new(path, Locations);
        foreach (KeyValuePair<ResourceLocation, EntityDefinition> pair in _byLocation)
        {
            clone._byLocation[pair.Key] = pair.Value;
        }

        foreach (KeyValuePair<int, EntityDefinition> pair in _byId)
        {
            clone._byId[pair.Key] = pair.Value;
        }

        clone._defaults = _defaults;
        clone.LoadPacksFrom(worldDatapackPath, LoadLocations.WorldDatapack);
        return clone;
    }

    public bool ContainsId(int id) => _byId.ContainsKey(id);
}
