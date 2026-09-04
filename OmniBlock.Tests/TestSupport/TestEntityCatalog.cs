using System.Diagnostics.CodeAnalysis;
using OmniBlock.Entities;
using OmniBlock.NBT;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Tests;

/// <summary>Concise test accessors; construction and identity always use the subject world's runtime.</summary>
internal static class TestEntityCatalog
{
    private static RuntimeEntityTypeRegistry Published => ContentRuntime.Current.EntityTypes;

    public static EntityType ByName(string name) => Published.Get(name);
    public static EntityType? BySpawnObjectId(int id) => Published.GetBySpawnObjectId(id);
    public static EntityType? ByGlobalSpawnId(int id) => Published.GetByGlobalSpawnId(id);

    public static Entity? Create(string id, IWorldContext world) =>
        world.Content.EntityTypes.TryCreate(id, world, out var entity) ? entity : null;

    public static Entity? Create(int id, IWorldContext world) =>
        world.Content.EntityTypes.TryCreate(id, world, out var entity) ? entity : null;

    public static bool TryCreate(string id, IWorldContext world, [MaybeNullWhen(false)] out Entity entity) =>
        world.Content.EntityTypes.TryCreate(id, world, out entity);

    public static bool TryCreate(int id, IWorldContext world, [MaybeNullWhen(false)] out Entity entity) =>
        world.Content.EntityTypes.TryCreate(id, world, out entity);

    public static int GetRawId(Entity entity) => entity.World.Content.EntityTypes.GetProtocolId(entity);
    public static string? GetId(Entity entity) => entity.World.Content.EntityTypes.GetKey(entity)?.Path;

    public static Entity? GetEntityFromNbt(NBTTagCompound nbt, IWorldContext world) =>
        world.Content.EntityTypes.ReadFromNbt(nbt, world);
}
