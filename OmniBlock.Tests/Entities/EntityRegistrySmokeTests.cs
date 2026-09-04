using OmniBlock.Entities;
using OmniBlock.Registries;

namespace OmniBlock.Tests.Entities;

[Collection("EntityTests")]
public sealed class EntityRegistrySmokeTests
{
    public static IEnumerable<object[]> RegistryEntityTypesExceptPlayer()
    {
        // Enumerates the registry itself now that TestEntityCatalog exposes no per-type static fields.
        foreach (ResourceLocation key in OmniBlock.Registries.ContentRuntime.Current.EntityTypes.Keys)
        {
            if (key.Path == "player") continue;

            yield return [key.Path, OmniBlock.Registries.ContentRuntime.Current.EntityTypes.Get(key)];
        }
    }

    [Theory]
    [MemberData(nameof(RegistryEntityTypesExceptPlayer))]
    public void TryCreate_string_id_round_trips(string registryFieldName, EntityType type)
    {
        FakeWorldContext world = new();
        int before = world.Entities.Entities.Count;
        Assert.True(TestEntityCatalog.TryCreate(type.Id.ToLowerInvariant(), world, out Entity? entity));
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
        int rawId = ContentRuntime.Current.EntityTypes.GetProtocolId(type);
        Assert.True(TestEntityCatalog.TryCreate(rawId, world, out Entity? entity));
        Assert.NotNull(entity);
        Assert.Same(type, entity.Type);
        Assert.Equal(rawId, TestEntityCatalog.GetRawId(entity));
        Assert.False(string.IsNullOrEmpty(registryFieldName));
        Assert.True(rawId >= 0);
    }

    [Fact]
    public void Player_factory_throws()
    {
        FakeWorldContext world = new();
        Assert.Throws<NotSupportedException>(() => TestEntityCatalog.ByName("player").Create(world));
    }
}
