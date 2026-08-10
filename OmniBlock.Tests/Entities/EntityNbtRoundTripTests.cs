using OmniBlock.Entities;
using OmniBlock.NBT;

namespace OmniBlock.Tests.Entities;

/// <summary>
/// Exercises <see cref="Entity.Write"/> / <see cref="Entity.Read"/> for each registry type to lift line coverage on entity-specific NBT and shared serialization.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityNbtRoundTripTests
{
    public static IEnumerable<object[]> RegistryEntityTypesExceptPlayer()
    {
        // Enumerates the registry itself now that EntityRegistry exposes no per-type static fields.
        foreach (ResourceLocation key in OmniBlock.Registries.DefaultRegistries.EntityTypes.Keys)
        {
            if (key.Path == "player") continue;

            yield return [OmniBlock.Registries.DefaultRegistries.EntityTypes.GetOrThrow(key)];
        }
    }

    [Theory]
    [MemberData(nameof(RegistryEntityTypesExceptPlayer))]
    public void SaveSelfNbt_round_trips_through_GetEntityFromNbt(EntityType type)
    {
        FakeWorldContext worldA = new();
        EntityTestHarness.PlaceStoneFloor(worldA, 0, 15, 0, 15, 63);

        Entity original = EntityTestHarness.CreateForNbtRoundTrip(type, worldA);
        var nbt = new NBTTagCompound();
        Assert.True(original.SaveSelfNbt(nbt));

        FakeWorldContext worldB = new();
        EntityTestHarness.PlaceStoneFloor(worldB, 0, 15, 0, 15, 63);

        Entity? loaded = EntityRegistry.GetEntityFromNbt(nbt, worldB);
        Assert.NotNull(loaded);
        Assert.Same(type, loaded!.Type);
        Assert.IsAssignableFrom(type.BaseType, loaded);
        Assert.Equal(original.X, loaded.X, 6);
        Assert.Equal(original.Y, loaded.Y, 6);
        Assert.Equal(original.Z, loaded.Z, 6);
    }
}
