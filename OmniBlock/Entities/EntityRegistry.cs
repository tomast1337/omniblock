using System.Diagnostics.CodeAnalysis;
using OmniBlock.NBT;
using OmniBlock.Registries;
using OmniBlock.Worlds.Core.Systems;
using Microsoft.Extensions.Logging;

namespace OmniBlock.Entities;

public static class EntityRegistry
{
    private static readonly ILogger s_logger = Log.Instance.For(nameof(EntityRegistry));
    private static readonly IRegistry<EntityType> s_registry = DefaultRegistries.EntityTypes;

    /// <summary>
    ///     Resolves the type an entity class was registered as, walking base classes so subclasses
    ///     that are not registered in their own right (the client's player entities) still resolve.
    ///     <para>
    ///         The fallback for entities constructed outside the registry; entities the registry
    ///         created carry their type instead (see <see cref="EntityType.Create" />). A class shared
    ///         by several registered types resolves to <c>null</c>, since it names none of them.
    ///     </para>
    /// </summary>
    public static EntityType? ByRuntimeType(Type runtimeType)
    {
        for (Type? candidate = runtimeType; candidate != null; candidate = candidate.BaseType)
        {
            EntityType[] matches = [.. s_registry.Where(type => type.BaseType == candidate)];
            if (matches.Length > 1) return null;
            if (matches.Length == 1) return matches[0];
        }

        return null;
    }

    public static EntityType ByName(string name) =>
        s_registry.GetOrThrow(ResourceLocation.Parse(name.ToLowerInvariant()));

    /// <summary>Whether a name resolves to a registered type, for callers validating user input.</summary>
    public static bool Exists(string name) =>
        s_registry.ContainsKey(ResourceLocation.Parse(name.ToLowerInvariant()));

    /// <summary>
    ///     Resolves a type by the object-spawn wire id its definition declares, or <c>null</c> if no
    ///     registered type claims it. This is how the client turns an object-spawn packet back into
    ///     an entity without a per-id branch.
    /// </summary>
    public static EntityType? BySpawnObjectId(int spawnObjectId)
    {
        foreach (EntityType type in s_registry)
        {
            if (type.Definition?.SpawnObjectId == spawnObjectId)
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>Same resolution for the global-entity spawn packet's own id space.</summary>
    public static EntityType? ByGlobalSpawnId(int globalSpawnId)
    {
        foreach (EntityType type in s_registry)
        {
            if (type.Definition?.GlobalSpawnId == globalSpawnId)
            {
                return type;
            }
        }

        return null;
    }

    public static Entity? Create(string id, IWorldContext world) => TryCreate(id, world, out Entity? entity) ? entity : null;

    public static bool TryCreate(string id, IWorldContext world, [MaybeNullWhen(false)] out Entity entity, EntityType? skip = null)
    {
        if (!s_registry.TryGet(ResourceLocation.Parse(id.ToLower()), out EntityType? type))
        {
            s_logger.LogInformation($"Unable to find entity with id {id}");
            entity = null;
            return false;
        }

        if (type == skip)
        {
            entity = null;
            return false;
        }

        entity = type.Create(world);
        return true;
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
        if (!s_registry.TryGet(ResourceLocation.Parse(name.ToLower()), out EntityType? entityType))
        {
            type = null;
            return false;
        }

        type = entityType.BaseType;
        return true;
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
