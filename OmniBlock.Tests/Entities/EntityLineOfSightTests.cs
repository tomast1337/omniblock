using OmniBlock.Blocks;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Util.Hit;
using OmniBlock.Util.Maths;

namespace OmniBlock.Tests.Entities;

/// <summary>
/// Covers the line-of-sight branches that were unreachable while <see cref="FakeWorldContext"/>
/// reported every raycast as a miss. The fake now runs the production traversal over its own block
/// grid, so a wall between two entities is a real wall.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityLineOfSightTests
{
    private static readonly int s_stone = BlockRegistry.Get("stone").Id;

    /// <summary>Builds a solid column at (x, z) tall enough to break a standing entity's sightline.</summary>
    private static void Wall(FakeWorldContext world, int x, int z)
    {
        for (int y = 63; y < 69; y++) world.ReaderWriter.SetBlock(x, y, z, s_stone, 0);
    }

    private static EntityCreature Spawn(FakeWorldContext world, string name, double x, double z)
    {
        EntityCreature mob = (EntityCreature)TestEntityCatalog.ByName(name).Create(world);
        mob.SetPositionAndAngles(x, 65.0, z, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(mob));
        return mob;
    }

    private static TestEntityPlayer Player(FakeWorldContext world, double x, double z)
    {
        TestEntityPlayer player = new(world) { Name = "tester" };
        player.SetPositionAndAngles(x, 65.0, z, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(player));
        return player;
    }

    [Fact]
    public void A_ray_through_air_misses_and_a_ray_into_a_block_hits()
    {
        FakeWorldContext world = new();
        Wall(world, 12, 8);

        Assert.Equal(HitResultType.Miss, world.Reader.Raycast(new Vec3D(8.5, 65.5, 8.5), new Vec3D(10.5, 65.5, 8.5)).Type);

        HitResult hit = world.Reader.Raycast(new Vec3D(8.5, 65.5, 8.5), new Vec3D(14.5, 65.5, 8.5));

        Assert.Equal(HitResultType.Tile, hit.Type);
        Assert.Equal(12, hit.BlockX);
    }

    [Fact]
    public void An_entity_cannot_see_through_a_wall()
    {
        FakeWorldContext world = new();
        EntityCreature zombie = Spawn(world, "zombie", 8.5, 8.5);
        TestEntityPlayer player = Player(world, 14.5, 8.5);

        Assert.True(zombie.CanSee(player));

        Wall(world, 11, 8);

        Assert.False(zombie.CanSee(player));
    }

    /// <summary>
    ///     The rejection branch in <see cref="Behaviors.AlwaysHuntTargetBehavior"/>: the player is
    ///     well within range, so only the sightline check can turn the target down.
    /// </summary>
    [Fact]
    public void Always_hunt_targeting_rejects_a_player_behind_a_wall()
    {
        FakeWorldContext world = new();
        EntityCreature zombie = Spawn(world, "zombie", 8.5, 8.5);
        Player(world, 14.5, 8.5);

        Assert.NotNull(zombie.Targeting!.FindPlayerToAttack(zombie));

        Wall(world, 11, 8);

        Assert.Null(zombie.Targeting.FindPlayerToAttack(zombie));
    }

    /// <summary>
    ///     Darkness-only targeting deliberately skips the sightline check, so a spider acquires
    ///     through a wall where a zombie does not. That difference was invisible while every raycast
    ///     missed, and is the whole reason the two behaviors are separate.
    /// </summary>
    [Fact]
    public void A_spider_targets_through_a_wall_where_a_zombie_cannot()
    {
        // A world each, so the wall sits squarely on the one sightline being tested.
        Assert.NotNull(TargetThroughWall("spider"));
        Assert.Null(TargetThroughWall("zombie"));
    }

    private static Entity? TargetThroughWall(string name)
    {
        FakeWorldContext world = new();
        EntityCreature mob = Spawn(world, name, 8.5, 8.5);
        Player(world, 14.5, 8.5);
        Wall(world, 11, 8);

        return mob.Targeting!.FindPlayerToAttack(mob);
    }

    /// <summary>A slime cannot damage a player it has no line to, even standing at contact range.</summary>
    [Fact]
    public void Slime_contact_damage_needs_a_clear_line()
    {
        FakeWorldContext world = new();
        EntityLiving slime = (EntityLiving)TestEntityCatalog.ByName("slime").Create(world);
        slime.Behaviors.Find<SizedBodyBehavior>()!.SetSize(slime, 4);
        slime.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(slime));

        TestEntityPlayer player = Player(world, 9.5, 8.5);
        int before = player.Health;

        Wall(world, 9, 8);
        slime.OnPlayerInteraction(player);
        Assert.Equal(before, player.Health);
    }
}
