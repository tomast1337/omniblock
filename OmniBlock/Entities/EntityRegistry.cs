using System.Diagnostics.CodeAnalysis;
using OmniBlock.NBT;
using OmniBlock.Registries;
using OmniBlock.Worlds.Core.Systems;
using Microsoft.Extensions.Logging;

namespace OmniBlock.Entities;

public static class EntityRegistry
{
    private static readonly ILogger s_logger = Log.Instance.For(nameof(EntityRegistry));
    private static RuntimeEntityTypeRegistry Runtime => ContentRuntime.Current.EntityTypes;

    /// <summary>
    ///     Resolves the type an entity class was registered as, walking base classes so subclasses
    ///     that are not registered in their own right (the client's player entities) still resolve.
    ///     <para>
    ///         The fallback for entities constructed outside the registry; entities the registry
    ///         created carry their type instead (see <see cref="EntityType.Create" />). A class shared
    ///         by several registered types resolves to <c>null</c>, since it names none of them.
    ///     </para>
    /// </summary>
    public static EntityType? ByRuntimeType(Type runtimeType) => Runtime.GetByRuntimeType(runtimeType);

    public static EntityType ByName(string name) =>
        Runtime.Get(name);

    /// <summary>Whether a name resolves to a registered type, for callers validating user input.</summary>
    public static bool Exists(string name) =>
        Runtime.TryGet(name, out _);

    /// <summary>
    ///     Resolves a type by the object-spawn wire id its definition declares, or <c>null</c> if no
    ///     registered type claims it. This is how the client turns an object-spawn packet back into
    ///     an entity without a per-id branch.
    /// </summary>
    public static EntityType? BySpawnObjectId(int spawnObjectId) => Runtime.GetBySpawnObjectId(spawnObjectId);

    /// <summary>Same resolution for the global-entity spawn packet's own id space.</summary>
    public static EntityType? ByGlobalSpawnId(int globalSpawnId) => Runtime.GetByGlobalSpawnId(globalSpawnId);

    public static Entity? Create(string id, IWorldContext world) => TryCreate(id, world, out Entity? entity) ? entity : null;

    public static bool TryCreate(string id, IWorldContext world, [MaybeNullWhen(false)] out Entity entity, EntityType? skip = null)
    {
        if (!world.Content.EntityTypes.TryCreate(id, world, out entity, skip))
        {
            s_logger.LogInformation($"Unable to find entity with id {id}");
            return false;
        }
        return true;
    }

    public static Entity? Create(int rawId, IWorldContext world) => TryCreate(rawId, world, out Entity? entity) ? entity : null;

    public static bool TryCreate(int rawId, IWorldContext world, [MaybeNullWhen(false)] out Entity entity)
    {
        if (world.Content.EntityTypes.TryCreate(rawId, world, out entity)) return true;

        s_logger.LogInformation($"Unable to find entity with raw id {rawId}");
        entity = null;
        return false;
    }

    public static int GetRawId(Entity entity) => entity.World.Content.EntityTypes.GetProtocolId(entity);

    public static string? GetId(Entity entity) => entity.World.Content.EntityTypes.GetKey(entity)?.Path;

    public static bool TryGetTypeFromName(string name, [MaybeNullWhen(false)] out Type type)
    {
        if (!Runtime.TryGet(name, out EntityType? entityType))
        {
            type = null;
            return false;
        }

        type = entityType.BaseType;
        return true;
    }

    public static Entity? GetEntityFromNbt(NBTTagCompound nbt, IWorldContext world)
    {
        return world.Content.EntityTypes.ReadFromNbt(nbt, world);
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
