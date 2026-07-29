using BetaSharp.Entities;
using BetaSharp.Registries;

namespace BetaSharp.Tests.Entities;

[Collection("EntityTests")]
public sealed class EntityRegistrySmokeTests
{
    public static IEnumerable<object[]> RegistryEntityTypesExceptPlayer()
    {
        // Enumerates the registry itself now that EntityRegistry exposes no per-type static fields.
        foreach (ResourceLocation key in BetaSharp.Registries.DefaultRegistries.EntityTypes.Keys)
        {
            if (key.Path == "player") continue;

            yield return [key.Path, BetaSharp.Registries.DefaultRegistries.EntityTypes.GetOrThrow(key)];
        }
    }

    [Theory]
    [MemberData(nameof(RegistryEntityTypesExceptPlayer))]
    public void TryCreate_string_id_round_trips(string registryFieldName, EntityType type)
    {
        FakeWorldContext world = new();
        int before = world.Entities.Entities.Count;
        Assert.True(EntityRegistry.TryCreate(type.Id.ToLowerInvariant(), world, out Entity? entity));
        Assert.NotNull(entity);
        Assert.Same(type, entity.Type);
        Assert.IsAssignableFrom(type.BaseType, entity);
        Assert.False(string.IsNullOrEmpty(registryFieldName));
        Assert.Equal(before, world.Entities.Entities.Count);
    }

    [Theory]
    [MemberData(nameof(RegistryEntityTypesExceptPlayer))]
    public void TryCreate_raw_id_round_trips(string registryFieldName, EntityType type)
    {
        FakeWorldContext world = new();
        int rawId = DefaultRegistries.EntityTypes.GetId(type);
        Assert.True(EntityRegistry.TryCreate(rawId, world, out Entity? entity));
        Assert.NotNull(entity);
        Assert.Same(type, entity.Type);
        Assert.Equal(rawId, EntityRegistry.GetRawId(entity));
        Assert.False(string.IsNullOrEmpty(registryFieldName));
        Assert.True(rawId >= 0);
    }

    [Fact]
    public void Player_factory_throws()
    {
        FakeWorldContext world = new();
        Assert.Throws<NotSupportedException>(() => EntityRegistry.ByName("player").Create(world));
    }
}
