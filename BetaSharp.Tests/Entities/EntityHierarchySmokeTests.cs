using BetaSharp.Entities;

namespace BetaSharp.Tests.Entities;

/// <summary>Smoke checks that concrete entity types sit under the expected abstract bases (regression guard for refactors).</summary>
[Collection("EntityTests")]
public sealed class EntityHierarchySmokeTests
{
    private readonly FakeWorldContext _world = new();

    [Fact]
    public void Mobs_and_projectiles_follow_expected_bases()
    {
        var zombie = (EntityMonster)EntityRegistry.ByName("zombie").Create(_world);
        Assert.IsAssignableFrom<EntityCreature>(zombie);
        Assert.IsAssignableFrom<EntityMonster>(zombie);
        Assert.IsAssignableFrom<EntityLiving>(zombie);

        var pig = (EntityAnimal)EntityRegistry.ByName("pig").Create(_world);
        Assert.IsAssignableFrom<EntityAnimal>(pig);
        Assert.IsAssignableFrom<EntityCreature>(pig);

        // Like the ghast: no class, no water-mob base. Its swimming, its spawn rule and its water
        // test all come from behavior slots.
        var squid = (EntityLiving)EntityRegistry.ByName("squid").Create(_world);
        Assert.Equal(typeof(EntityLiving), squid.GetType());

        // The ghast has no class and no flying base: it is a bare EntityLiving whose flight,
        // wandering and fireballs all come from its behavior slots.
        var ghast = (EntityLiving)EntityRegistry.ByName("ghast").Create(_world);
        Assert.Equal(typeof(EntityLiving), ghast.GetType());

        Assert.IsAssignableFrom<Entity>(new EntityArrow(_world));
        Assert.IsAssignableFrom<Entity>(new EntitySnowball(_world));
        Assert.False(zombie.Dead);
        Assert.False(pig.Dead);
        Assert.False(squid.Dead);
        Assert.False(ghast.Dead);
    }

    [Fact]
    public void Weather_effect_base()
    {
        Assert.IsAssignableFrom<EntityWeatherEffect>(new EntityLightningBolt(_world));
    }
}
