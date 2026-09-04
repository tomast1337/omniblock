using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using OmniBlock.NBT;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Entities;

/// <summary>Immutable indexes and construction operations for one finalized entity catalog.</summary>
public sealed class RuntimeEntityTypeRegistry : IEntityTypeBuildView
{
    private static readonly ILogger s_logger = Log.Instance.For<RuntimeEntityTypeRegistry>();
    private readonly FrozenDictionary<int, EntityType> _byGlobalSpawnId;
    private readonly FrozenDictionary<ResourceLocation, EntityType> _byKey;
    private readonly FrozenDictionary<int, EntityType> _byProtocolId;
    private readonly FrozenDictionary<int, EntityType> _bySpawnObjectId;
    private readonly FrozenDictionary<EntityType, ResourceLocation> _keysByType;
    private readonly FrozenDictionary<EntityType, int> _protocolIdsByType;

    internal RuntimeEntityTypeRegistry(
        IEnumerable<(ResourceLocation Key, int ProtocolId, EntityType Type)> entries)
    {
        var byKey = new Dictionary<ResourceLocation, EntityType>();
        var byProtocolId = new Dictionary<int, EntityType>();
        var bySpawnObjectId = new Dictionary<int, EntityType>();
        var byGlobalSpawnId = new Dictionary<int, EntityType>();
        var keysByType = new Dictionary<EntityType, ResourceLocation>();
        var protocolIdsByType = new Dictionary<EntityType, int>();

        foreach (var (key, protocolId, type) in entries)
        {
            if (!byKey.TryAdd(key, type)) throw new ArgumentException($"Duplicate entity key '{key}'.");
            if (!byProtocolId.TryAdd(protocolId, type))
                throw new ArgumentException($"Duplicate entity protocol id {protocolId} for '{key}'.");
            keysByType.Add(type, key);
            protocolIdsByType.Add(type, protocolId);

            AddOptional(bySpawnObjectId, type.Definition?.SpawnObjectId ?? 0, "object-spawn", key, type);
            AddOptional(byGlobalSpawnId, type.Definition?.GlobalSpawnId ?? 0, "global-spawn", key, type);
        }

        _byKey = byKey.ToFrozenDictionary();
        _byProtocolId = byProtocolId.ToFrozenDictionary();
        _bySpawnObjectId = bySpawnObjectId.ToFrozenDictionary();
        _byGlobalSpawnId = byGlobalSpawnId.ToFrozenDictionary();
        _keysByType = keysByType.ToFrozenDictionary();
        _protocolIdsByType = protocolIdsByType.ToFrozenDictionary();
    }

    public int Count => _byKey.Count;
    public IEnumerable<ResourceLocation> Keys => _byKey.Keys;
    public IEnumerable<EntityType> Values => _byKey.Values;

    public EntityType Get(ResourceLocation key) => _byKey.TryGetValue(key, out var type)
        ? type
        : throw new KeyNotFoundException($"Unknown entity type '{key}'.");

    public bool TryGet(ResourceLocation key, [NotNullWhen(true)] out EntityType? type) => _byKey.TryGetValue(key, out type);
    public EntityType Get(string key) => Get(ParseKey(key));
    public bool TryGet(string key, [NotNullWhen(true)] out EntityType? type) => TryGet(ParseKey(key), out type);

    public EntityType GetByProtocolId(int id) => _byProtocolId.TryGetValue(id, out var type)
        ? type
        : throw new KeyNotFoundException($"Unknown entity protocol id {id}.");

    public bool TryGetByProtocolId(int id, [NotNullWhen(true)] out EntityType? type) => _byProtocolId.TryGetValue(id, out type);
    public EntityType? GetBySpawnObjectId(int id) => _bySpawnObjectId.GetValueOrDefault(id);
    public EntityType? GetByGlobalSpawnId(int id) => _byGlobalSpawnId.GetValueOrDefault(id);

    public Entity Create(ResourceLocation key, IWorldContext world) => Get(key).Create(world);
    public Entity Create(string key, IWorldContext world) => Create(ParseKey(key), world);
    public Entity CreateByProtocolId(int protocolId, IWorldContext world) => GetByProtocolId(protocolId).Create(world);

    public bool TryCreate(string key, IWorldContext world, [MaybeNullWhen(false)] out Entity entity, EntityType? skip = null)
    {
        if (!TryGet(key, out var type) || ReferenceEquals(type, skip))
        {
            entity = null;
            return false;
        }

        entity = type.Create(world);
        return true;
    }

    public bool TryCreate(int protocolId, IWorldContext world, [MaybeNullWhen(false)] out Entity entity)
    {
        if (!TryGetByProtocolId(protocolId, out var type))
        {
            entity = null;
            return false;
        }

        entity = type.Create(world);
        return true;
    }

    public int GetProtocolId(Entity entity) =>
        entity.Type is { } type && _protocolIdsByType.TryGetValue(type, out var id) ? id : -1;

    public int GetProtocolId(EntityType type) => _protocolIdsByType.TryGetValue(type, out var id) ? id : -1;

    public ResourceLocation? GetKey(Entity entity) =>
        entity.Type is { } type ? _keysByType.GetValueOrDefault(type) : null;

    public Entity? ReadFromNbt(
        NBTTagCompound nbt,
        IWorldContext world,
        UnknownEntityLoadPolicy unknownPolicy = UnknownEntityLoadPolicy.SkipWithWarning,
        Action<string>? reportWarning = null)
    {
        var persistedId = nbt.GetString("id");
        EntityType? type = null;
        try
        {
            TryGet(persistedId, out type);
        }
        catch (Exception error) when (error is ArgumentException or FormatException)
        {
            // Invalid resource names follow the same policy as names whose defining mod is absent.
        }

        if (type is null)
        {
            var diagnostic =
                $"Cannot load persisted entity type '{persistedId}': it is not present in the content catalog; the defining mod may be missing.";
            if (unknownPolicy == UnknownEntityLoadPolicy.Fail)
                throw new InvalidOperationException(diagnostic);
            (reportWarning ?? (message => s_logger.LogWarning(message)))(diagnostic);
            return null;
        }

        if (ReferenceEquals(type, Get("omniblock:player"))) return null;
        var entity = type.Create(world);
        entity.Read(nbt);
        return entity;
    }

    private static ResourceLocation ParseKey(string key) => ResourceLocation.Parse(key.ToLowerInvariant());

    private static void AddOptional(
        IDictionary<int, EntityType> index, int id, string kind, ResourceLocation key, EntityType type)
    {
        if (id != 0 && !index.TryAdd(id, type))
            throw new ArgumentException($"Duplicate entity {kind} id {id} for '{key}'.");
    }
}

public enum UnknownEntityLoadPolicy
{
    SkipWithWarning,
    Fail
}
