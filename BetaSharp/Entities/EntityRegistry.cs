using System.Diagnostics.CodeAnalysis;
using BetaSharp.NBT;
using BetaSharp.Registries;
using BetaSharp.Worlds.Core.Systems;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Entities;

public static class EntityRegistry
{
    private static readonly ILogger s_logger = Log.Instance.For(nameof(EntityRegistry));
    private static readonly IRegistry<EntityType> s_registry = DefaultRegistries.EntityTypes;

    /// <summary>
    ///     Registration runs from the static constructor, triggered by
    ///     <c>DefaultRegistries.EntityTypes.Bootstrap(typeof(EntityRegistry))</c>. There are no
    ///     per-type static accessors: callers resolve types through <see cref="ByName" />.
    ///     <para>
    ///         Mobs take their protocol id from <c>assets/entity/*.json</c> rather than a literal
    ///         here — the id is part of the definition, so declaring it twice would let them drift.
    ///     </para>
    /// </summary>
    static EntityRegistry()
    {
        RegisterDefined((world, type) => new EntityObject(world, type), "Arrow");
        RegisterDefined((world, type) => new EntityObject(world, type), "Snowball");
        RegisterDefined((world, type) => new EntityObject(world, type), "Item");
        RegisterDefined((world, type) => new EntityObject(world, type), "Painting");
        RegisterDefined((world, type) => new EntityMonster(world, type), "Creeper");
        RegisterDefined((world, type) => new EntityMonster(world, type), "Skeleton");
        RegisterDefined((world, type) => new EntityMonster(world, type), "Spider");
        RegisterDefined((world, type) => new EntityMonster(world, type), "Giant");
        RegisterDefined((world, type) => new EntityMonster(world, type), "Zombie");
        RegisterDefined((world, type) => new EntityLiving(world, type), "Slime");
        RegisterDefined((world, type) => new EntityLiving(world, type), "Ghast");
        RegisterDefined((world, type) => new EntityMonster(world, type), "PigZombie");
        RegisterDefined((world, type) => new EntityAnimal(world, type), "Pig");
        RegisterDefined((world, type) => new EntityAnimal(world, type), "Sheep");

        // No class of its own: a cow is an EntityAnimal configured by cow.json. Every behavior it
        // once overrode now sits in a capability slot, so the subclass had nothing left to hold.
        RegisterDefined((world, type) => new EntityAnimal(world, type), "Cow");
        RegisterDefined((world, type) => new EntityAnimal(world, type), "Chicken");
        RegisterDefined((world, type) => new EntityLiving(world, type), "Squid");
        RegisterDefined((world, type) => new EntityAnimal(world, type), "Wolf");
        // No class of their own either: primed TNT and falling sand are EntityObjects configured by
        // their JSON — the non-living entities on the same footing as the mobs.
        RegisterDefined((world, type) => new EntityObject(world, type), "PrimedTnt");
        RegisterDefined((world, type) => new EntityObject(world, type), "FallingSand");
        Register((world, _) => new EntityMinecart(world), "Minecart", 40);
        Register((world, _) => new EntityBoat(world), "Boat", 41);
        RegisterDefined((world, type) => new EntityObject(world, type), "Egg");
        RegisterDefined((world, type) => new EntityObject(world, type), "Fireball");
        Register((world, _) => new EntityFish(world), "FishHook", 64);
        RegisterDefined((world, type) => new EntityObject(world, type), "LightningBolt");
        Register<ServerPlayerEntity>((_, _) => throw new NotSupportedException("Players must be created via ServerPlayerEntity constructor"), "Player", 100);
    }

    /// <summary>
    ///     Registers an entity that is fully described by data — mob or not — taking both its
    ///     configuration and its protocol id from the JSON definition of the same (lowercased) name.
    /// </summary>
    private static EntityType RegisterDefined<T>(Func<IWorldContext, EntityType, T> factory, string id) where T : Entity
    {
        EntityDefinition definition = EntityDefinitionRegistry.Get(id.ToLowerInvariant());
        return Register(factory, id, definition.ProtocolId, definition);
    }

    private static EntityType Register<T>(Func<IWorldContext, EntityType, T> factory, string id, int rawId, EntityDefinition? definition = null) where T : Entity
    {
        // Spawn packets transmit this as a signed byte, so an out-of-range id would be silently
        // truncated into a different entity on the wire. Fail at registration instead.
        if (rawId is < sbyte.MinValue or > sbyte.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rawId),
                rawId,
                $"Protocol id for entity '{id}' must fit in a signed byte ({sbyte.MinValue}..{sbyte.MaxValue}).");
        }

        EntityType type = new((w, t) => factory(w, t), typeof(T), id, definition);
        s_registry.Register(rawId, ResourceLocation.Parse(id.ToLower()), type);

        // Several types may now share one class (every plain animal is an EntityAnimal). A class that
        // maps to more than one type identifies nothing, so it is struck from the index rather than
        // left resolving to whichever registration happened to run first.
        if (!s_byRuntimeType.TryAdd(typeof(T), type)) s_ambiguousRuntimeTypes.Add(typeof(T));

        return type;
    }

    /// <summary>
    ///     Resolves a registered type by its registry path (e.g. <c>"zombie"</c>). Replaces the
    ///     per-type static accessors this class used to expose, mirroring <c>Item.ByName</c>.
    ///     <para>
    ///         Callers on a hot path should cache the result in a <c>static readonly</c> field
    ///         rather than resolving per call — the same treatment the item migration gave its own
    ///         hot <c>ByName</c> lookups.
    ///     </para>
    /// </summary>
    private static readonly Dictionary<Type, EntityType> s_byRuntimeType = [];
    private static readonly HashSet<Type> s_ambiguousRuntimeTypes = [];

    /// <summary>
    ///     Resolves the type an entity class was registered as, walking base classes so subclasses
    ///     that are not registered in their own right (the client's player entities) still resolve.
    ///     <para>
    ///         This is the fallback for entities constructed outside the registry. Entities the
    ///         registry created carry their type instead — see <see cref="EntityType.Create" />.
    ///         Classes shared by several registered types resolve to <c>null</c>, because the class
    ///         no longer says which one it is.
    ///     </para>
    /// </summary>
    public static EntityType? ByRuntimeType(Type runtimeType)
    {
        for (Type? candidate = runtimeType; candidate != null; candidate = candidate.BaseType)
        {
            if (s_ambiguousRuntimeTypes.Contains(candidate)) return null;
            if (s_byRuntimeType.TryGetValue(candidate, out EntityType? type)) return type;
        }

        return null;
    }

    public static EntityType ByName(string name) =>
        s_registry.Get(ResourceLocation.Parse(name.ToLowerInvariant()))
        ?? throw new ArgumentException($"Unknown entity type: '{name}'", nameof(name));

    /// <summary>
    ///     Resolves a type by the object-spawn wire id its definition declares, or <c>null</c> if no
    ///     registered type claims it. This is how the client turns an object-spawn packet back into
    ///     an entity without a per-id branch.
    /// </summary>
    public static EntityType? BySpawnObjectId(int spawnObjectId)
    {
        foreach (EntityType type in s_registry)
        {
            if (type.Definition?.SpawnObjectId == spawnObjectId) return type;
        }

        return null;
    }

    /// <summary>Same resolution for the global-entity spawn packet's own id space.</summary>
    public static EntityType? ByGlobalSpawnId(int globalSpawnId)
    {
        foreach (EntityType type in s_registry)
        {
            if (type.Definition?.GlobalSpawnId == globalSpawnId) return type;
        }

        return null;
    }

    public static Entity? Create(string id, IWorldContext world) => TryCreate(id, world, out Entity? entity) ? entity : null;

    public static bool TryCreate(string id, IWorldContext world, [MaybeNullWhen(false)] out Entity entity, EntityType? skip = null)
    {
        EntityType? type = s_registry.Get(ResourceLocation.Parse(id.ToLower()));

        if (type == skip)
        {
            entity = null;
            return false;
        }

        if (type != null)
        {
            entity = type.Create(world);
            return true;
        }

        s_logger.LogInformation($"Unable to find entity with id {id}");
        entity = null;
        return false;
    }

    public static Entity? Create(int rawId, IWorldContext world) => TryCreate(rawId, world, out Entity? entity) ? entity : null;

    public static bool TryCreate(int rawId, IWorldContext world, [MaybeNullWhen(false)] out Entity entity)
    {
        EntityType? type = s_registry.Get(rawId);
        if (type != null)
        {
            entity = type.Create(world);
            return true;
        }

        s_logger.LogInformation($"Unable to find entity with raw id {rawId}");
        entity = null;
        return false;
    }

    public static int GetRawId(Entity entity) => entity.Type != null ? s_registry.GetId(entity.Type) : -1;

    public static string? GetId(Entity entity) => entity.Type != null ? s_registry.GetKey(entity.Type)?.Path : null;

    public static bool TryGetTypeFromName(string name, [MaybeNullWhen(false)] out Type type)
    {
        EntityType? entityType = s_registry.Get(ResourceLocation.Parse(name.ToLower()));
        if (entityType != null)
        {
            type = entityType.BaseType;
            return true;
        }

        type = null;
        return false;
    }

    public static Entity? GetEntityFromNbt(NBTTagCompound nbt, IWorldContext world)
    {
        string id = nbt.GetString("id");
        if (TryCreate(id, world, out Entity? entity, ByName("player")))
        {
            entity!.Read(nbt);
        }

        return entity;
    }

    public static Entity? CreateEntityAt(string name, IWorldContext world, float x, float y, float z)
    {
        name = name.ToLower();
        if (TryCreate(name, world, out Entity? entity))
        {
            entity.SetPosition(x, y, z);
            entity.SetPositionAndAngles(x, y, z, 0, 0);
            if (!world.SpawnEntity(entity))
            {
                s_logger.LogError($"Entity `{name}` failed to join world.");
            }

            return entity;
        }

        s_logger.LogError($"Failed to find entity type associated with name `{name}`");
        return null;
    }
}
