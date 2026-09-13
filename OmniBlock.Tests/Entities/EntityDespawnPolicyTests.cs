using System.Reflection;
using OmniBlock.Entities;
using OmniBlock.Tests.TestSupport;

namespace OmniBlock.Tests.Entities;

[Collection("EntityTests")]
public sealed class EntityDespawnPolicyTests
{
    [Theory]
    [InlineData("cow")]
    [InlineData("pig")]
    [InlineData("sheep")]
    [InlineData("chicken")]
    [InlineData("wolf")]
    public void Passive_land_mobs_are_persistent(string type)
    {
        var mob = (EntityLiving)TestEntityCatalog.ByName(type).Create(new FakeWorldContext());
        Assert.False(CanDespawn(mob));
    }

    [Theory]
    [InlineData("zombie")]
    [InlineData("skeleton")]
    [InlineData("creeper")]
    [InlineData("squid")]
    public void Hostile_and_water_population_still_uses_distance_cleanup(string type)
    {
        var mob = (EntityLiving)TestEntityCatalog.ByName(type).Create(new FakeWorldContext());
        Assert.True(CanDespawn(mob));
    }

    [Fact]
    public void Despawn_records_a_distinct_removal_reason_without_redefining_death()
    {
        var mob = (EntityLiving)TestEntityCatalog.ByName("creeper").Create(new FakeWorldContext());

        mob.Despawn();

        Assert.True(mob.Dead);
        Assert.Equal(EntityRemovalReason.DistanceDespawn, mob.RemovalReason);
    }

    private static bool CanDespawn(EntityLiving entity) =>
        (bool)typeof(EntityLiving).GetProperty("CanDespawn",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entity)!;
}
