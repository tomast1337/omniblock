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
        Register(world => new EntityArrow(world), "Arrow", 10);
        Register(world => new EntitySnowball(world), "Snowball", 11);
        Register(world => new EntityItem(world), "Item", 1);
        Register(world => new EntityPainting(world), "Painting", 9);
        RegisterMob(world => new EntityCreeper(world), "Creeper");
        RegisterMob(world => new EntitySkeleton(world), "Skeleton");
        RegisterMob(world => new EntitySpider(world), "Spider");
        RegisterMob(world => new EntityGiantZombie(world), "Giant");
        RegisterMob(world => new EntityZombie(world), "Zombie");
        RegisterMob(world => new EntitySlime(world), "Slime");
        RegisterMob(world => new EntityGhast(world), "Ghast");
        RegisterMob(world => new EntityPigZombie(world), "PigZombie");
        RegisterMob(world => new EntityPig(world), "Pig");
        RegisterMob(world => new EntitySheep(world), "Sheep");
        RegisterMob(world => new EntityCow(world), "Cow");
        RegisterMob(world => new EntityChicken(world), "Chicken");
        RegisterMob(world => new EntitySquid(world), "Squid");
        RegisterMob(world => new EntityWolf(world), "Wolf");
        Register(world => new EntityTntPrimed(world), "PrimedTnt", 20);
        Register(world => new EntityFallingSand(world), "FallingSand", 21);
        Register(world => new EntityMinecart(world), "Minecart", 40);
        Register(world => new EntityBoat(world), "Boat", 41);
        Register(world => new EntityEgg(world), "Egg", 62);
        Register(world => new EntityFireball(world), "Fireball", 63);
        Register(world => new EntityFish(world), "FishHook", 64);
        Register(world => new EntityLightningBolt(world), "LightningBolt", 65);
        Register<ServerPlayerEntity>(_ => throw new NotSupportedException("Players must be created via ServerPlayerEntity constructor"), "Player", 100);
    }

    /// <summary>
    ///     Registers a mob, taking both its configuration and its protocol id from the JSON
    ///     definition of the same (lowercased) name.
    /// </summary>
    private static EntityType RegisterMob<T>(Func<IWorldContext, T> factory, string id) where T : Entity
    {
        EntityDefinition definition = EntityDefinitionRegistry.Get(id.ToLowerInvariant());
        return Register(factory, id, definition.ProtocolId, definition);
    }

    private static EntityType Register<T>(Func<IWorldContext, T> factory, string id, int rawId, EntityDefinition? definition = null) where T : Entity
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

        EntityType type = new(w => factory(w), typeof(T), id, definition);
        s_registry.Register(rawId, ResourceLocation.Parse(id.ToLower()), type);
        s_byRuntimeType[typeof(T)] = type;
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

    /// <summary>
    ///     Resolves the type an entity class was registered as, walking base classes so subclasses
    ///     that are not registered in their own right (the client's player entities) still resolve.
    ///     Backs <see cref="Entity.Type" />, replacing a per-class override on every entity.
    /// </summary>
    public static EntityType? ByRuntimeType(Type runtimeType)
    {
        for (Type? candidate = runtimeType; candidate != null; candidate = candidate.BaseType)
        {
            if (s_byRuntimeType.TryGetValue(candidate, out EntityType? type)) return type;
        }

        return null;
    }

    public static EntityType ByName(string name) =>
        s_registry.Get(ResourceLocation.Parse(name.ToLowerInvariant()))
        ?? throw new ArgumentException($"Unknown entity type: '{name}'", nameof(name));

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
