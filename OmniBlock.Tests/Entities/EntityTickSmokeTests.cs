using OmniBlock.Entities;

namespace OmniBlock.Tests.Entities;

/// <summary>Runs a short simulated tick loop for each registry-spawnable entity to catch immediate exceptions.</summary>
[Collection("EntityTests")]
public sealed class EntityTickSmokeTests
{
    public static IEnumerable<object[]> RegistryEntityTypesExceptPlayer()
    {
        // Enumerates the registry itself now that TestEntityCatalog exposes no per-type static fields.
        foreach (var key in ContentRuntime.Current.EntityTypes.Keys)
        {
            if (key.Path == "player") continue;

            yield return [key.Path, ContentRuntime.Current.EntityTypes.Get(key)];
        }
    }

    [Theory]
    [MemberData(nameof(RegistryEntityTypesExceptPlayer))]
    public void Spawn_on_stone_floor_then_tick_does_not_throw(string registryFieldName, EntityType type)
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceStoneFloor(world, 0, 15, 0, 15, 63);
        var entity = EntityTestHarness.CreateSpawned(world, type, 8.5, 65.0, 8.5);
        var beforeTicksAlive = EntityTestHarness.AliveEntityCount(world);
        EntityTestHarness.AdvanceGameTicks(world, 64);
        Assert.False(string.IsNullOrEmpty(registryFieldName));
        Assert.NotNull(entity);
        Assert.IsAssignableFrom(type.BaseType, entity);
        Assert.True(beforeTicksAlive >= 1);
        Assert.True(EntityTestHarness.AliveEntityCount(world) >= 0);
    }
}
