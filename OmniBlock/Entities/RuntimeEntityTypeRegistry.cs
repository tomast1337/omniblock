using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using OmniBlock.NBT;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Entities;

/// <summary>Immutable indexes and construction operations for one finalized entity catalog.</summary>
public sealed class RuntimeEntityTypeRegistry : IEntityTypeBuildView
{
    private readonly FrozenDictionary<ResourceLocation, EntityType> _byKey;
    private readonly FrozenDictionary<int, EntityType> _byProtocolId;
    private readonly FrozenDictionary<int, EntityType> _bySpawnObjectId;
    private readonly FrozenDictionary<int, EntityType> _byGlobalSpawnId;
    private readonly FrozenDictionary<Type, EntityType> _byUnambiguousRuntimeType;
    private readonly FrozenDictionary<EntityType, ResourceLocation> _keysByType;
    private readonly FrozenDictionary<EntityType, int> _protocolIdsByType;

    internal RuntimeEntityTypeRegistry(
        IEnumerable<(ResourceLocation Key, int ProtocolId, EntityType Type)> entries)
    {
        var byKey = new Dictionary<ResourceLocation, EntityType>();
        var byProtocolId = new Dictionary<int, EntityType>();
        var bySpawnObjectId = new Dictionary<int, EntityType>();
        var byGlobalSpawnId = new Dictionary<int, EntityType>();
        var runtimeCandidates = new Dictionary<Type, EntityType>();
        var ambiguousRuntimeTypes = new HashSet<Type>();
        var keysByType = new Dictionary<EntityType, ResourceLocation>();
        var protocolIdsByType = new Dictionary<EntityType, int>();

        foreach ((ResourceLocation key, int protocolId, EntityType type) in entries)
        {
            if (!byKey.TryAdd(key, type)) throw new ArgumentException($"Duplicate entity key '{key}'.");
            if (!byProtocolId.TryAdd(protocolId, type))
                throw new ArgumentException($"Duplicate entity protocol id {protocolId} for '{key}'.");
            keysByType.Add(type, key);
            protocolIdsByType.Add(type, protocolId);

            AddOptional(bySpawnObjectId, type.Definition?.SpawnObjectId ?? 0, "object-spawn", key, type);
            AddOptional(byGlobalSpawnId, type.Definition?.GlobalSpawnId ?? 0, "global-spawn", key, type);

            if (!runtimeCandidates.TryAdd(type.BaseType, type)) ambiguousRuntimeTypes.Add(type.BaseType);
        }

        foreach (Type ambiguous in ambiguousRuntimeTypes) runtimeCandidates.Remove(ambiguous);
        _byKey = byKey.ToFrozenDictionary();
        _byProtocolId = byProtocolId.ToFrozenDictionary();
        _bySpawnObjectId = bySpawnObjectId.ToFrozenDictionary();
        _byGlobalSpawnId = byGlobalSpawnId.ToFrozenDictionary();
        _byUnambiguousRuntimeType = runtimeCandidates.ToFrozenDictionary();
        _keysByType = keysByType.ToFrozenDictionary();
        _protocolIdsByType = protocolIdsByType.ToFrozenDictionary();
    }

    public int Count => _byKey.Count;
    public IEnumerable<ResourceLocation> Keys => _byKey.Keys;

    public EntityType Get(ResourceLocation key) => _byKey.TryGetValue(key, out EntityType? type)
        ? type : throw new KeyNotFoundException($"Unknown entity type '{key}'.");
    public EntityType Get(string key) => Get(ParseKey(key));
    public bool TryGet(ResourceLocation key, [NotNullWhen(true)] out EntityType? type) => _byKey.TryGetValue(key, out type);
    public bool TryGet(string key, [NotNullWhen(true)] out EntityType? type) => TryGet(ParseKey(key), out type);
    public EntityType GetByProtocolId(int id) => _byProtocolId.TryGetValue(id, out EntityType? type)
        ? type : throw new KeyNotFoundException($"Unknown entity protocol id {id}.");
    public bool TryGetByProtocolId(int id, [NotNullWhen(true)] out EntityType? type) => _byProtocolId.TryGetValue(id, out type);
    public EntityType? GetBySpawnObjectId(int id) => _bySpawnObjectId.GetValueOrDefault(id);
    public EntityType? GetByGlobalSpawnId(int id) => _byGlobalSpawnId.GetValueOrDefault(id);

    public EntityType? GetByRuntimeType(Type runtimeType)
    {
        for (Type? candidate = runtimeType; candidate is not null; candidate = candidate.BaseType)
            if (_byUnambiguousRuntimeType.TryGetValue(candidate, out EntityType? type)) return type;
        return null;
    }

    public Entity Create(ResourceLocation key, IWorldContext world) => Get(key).Create(world);
    public Entity Create(string key, IWorldContext world) => Create(ParseKey(key), world);
    public bool TryCreate(string key, IWorldContext world, [MaybeNullWhen(false)] out Entity entity, EntityType? skip = null)
    {
        if (!TryGet(key, out EntityType? type) || ReferenceEquals(type, skip))
        {
            entity = null;
            return false;
        }
        entity = type.Create(world);
        return true;
    }

    public bool TryCreate(int protocolId, IWorldContext world, [MaybeNullWhen(false)] out Entity entity)
    {
        if (!TryGetByProtocolId(protocolId, out EntityType? type))
        {
            entity = null;
            return false;
        }
        entity = type.Create(world);
        return true;
    }

    public int GetProtocolId(Entity entity) =>
        entity.Type is { } type && _protocolIdsByType.TryGetValue(type, out int id) ? id : -1;
    public ResourceLocation? GetKey(Entity entity) =>
        entity.Type is { } type ? _keysByType.GetValueOrDefault(type) : null;

    public Entity? ReadFromNbt(NBTTagCompound nbt, IWorldContext world)
    {
        if (!TryCreate(nbt.GetString("id"), world, out Entity? entity, Get("omniblock:player"))) return null;
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
