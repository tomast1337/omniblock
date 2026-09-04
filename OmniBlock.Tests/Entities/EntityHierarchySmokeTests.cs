using OmniBlock.Entities;

namespace OmniBlock.Tests.Entities;

/// <summary>Smoke checks that concrete entity types sit under the expected abstract bases (regression guard for refactors).</summary>
[Collection("EntityTests")]
public sealed class EntityHierarchySmokeTests
{
    private readonly FakeWorldContext _world = new();

    [Fact]
    public void Mobs_and_projectiles_follow_expected_bases()
    {
        // A monster and a farm animal are the same class now: what separates them is declared.
        var zombie = (EntityCreature)TestEntityCatalog.ByName("zombie").Create(_world);
        Assert.Equal(typeof(EntityCreature), zombie.GetType());
        Assert.IsAssignableFrom<EntityLiving>(zombie);

        var pig = (EntityCreature)TestEntityCatalog.ByName("pig").Create(_world);
        Assert.Equal(typeof(EntityCreature), pig.GetType());

        // Like the ghast: no class, no water-mob base. Its swimming, its spawn rule and its water
        // test all come from behavior slots.
        var squid = (EntityLiving)TestEntityCatalog.ByName("squid").Create(_world);
        Assert.Equal(typeof(EntityLiving), squid.GetType());

        // The ghast has no class and no flying base: it is a bare EntityLiving whose flight,
        // wandering and fireballs all come from its behavior slots.
        var ghast = (EntityLiving)TestEntityCatalog.ByName("ghast").Create(_world);
        Assert.Equal(typeof(EntityLiving), ghast.GetType());

        // Projectiles have no classes either: arrow, snowball and egg are all EntityObjects, the
        // first with its own behavior and the latter two sharing one.
        Assert.Equal(typeof(EntityObject), TestEntityCatalog.ByName("arrow").Create(_world).GetType());
        Assert.Equal(typeof(EntityObject), TestEntityCatalog.ByName("snowball").Create(_world).GetType());
        Assert.False(zombie.Dead);
        Assert.False(pig.Dead);
        Assert.False(squid.Dead);
        Assert.False(ghast.Dead);
    }

    /// <summary>
    ///     The weather-effect base class is gone with the lightning class: a bolt is an EntityObject
    ///     whose behavior is declared, like TNT and falling sand.
    /// </summary>
    [Fact]
    public void Lightning_is_a_plain_entity_object() => Assert.Equal(typeof(EntityObject), TestEntityCatalog.ByName("lightningbolt").Create(_world).GetType());
}
