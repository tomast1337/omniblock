using System.Text.Json;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.Entities.State;
using BetaSharp.Items;
using BetaSharp.Loot;
using BetaSharp.Loot.Conditions;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Covers the behaviors half of Phase 4: capability slots are declared in
/// <c>assets/entity/*.json</c> and built through <see cref="EntityBehaviorRegistry"/>, rather than
/// wired in mob constructors.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityBehaviorJsonTests
{
    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    /// <summary>Behaviors are now built with a load-time context, once per entity type.</summary>
    private static object Build(string json, EntityDefinition? definition = null) =>
        EntityBehaviorRegistry.Build(new EntityBehaviorContext(
            Json(json),
            definition ?? new EntityDefinition { ProtocolId = 1, Name = "test" },
            new EntityStateLayout()));

    [Fact]
    public void Registry_builds_each_attack_type_with_its_parameters()
    {
        Assert.IsType<MeleeAttackBehavior>(Build("""{"Type":"melee"}"""));
        Assert.IsType<RangedAttackBehavior>(Build("""{"Type":"ranged","range":12,"cooldown_ticks":15}"""));
        Assert.IsType<JumpAttackBehavior>(Build("""{"Type":"jump","min_range":2,"max_range":6,"chance_one_in":10}"""));
    }

    [Fact]
    public void Jump_attack_can_nest_a_fallback_behavior()
    {
        object built = Build("""{"Type":"jump","fallback":{"Type":"melee"}}""");
        JumpAttackBehavior jump = Assert.IsType<JumpAttackBehavior>(built);

        // The nested melee is what a spider falls back to outside its lunge band.
        FakeWorldContext world = new();
        EntitySpider spider = new(world);
        EntityPig target = new(world);
        spider.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        target.SetPositionAndAngles(9.0, 65.0, 8.5, 0f, 0f);
        int before = target.Health;

        jump.AttackEntity(spider, target, 1.0f); // inside melee range, outside the jump band
        Assert.True(target.Health < before);
    }

    [Fact]
    public void Unknown_behavior_type_fails_loudly()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => Build("""{"Type":"teleport"}"""));
        Assert.Contains("teleport", error.Message);
    }

    [Fact]
    public void Unknown_loot_condition_fails_loudly()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => LootJson.ParseCondition(Json("""{"type":"phase_of_the_moon"}""")));
        Assert.Contains("phase_of_the_moon", error.Message);
    }

    [Theory]
    [InlineData("""{"type":"killed_by_skeleton"}""", typeof(KilledByCondition<EntitySkeleton>))]
    [InlineData("""{"type":"on_fire"}""", typeof(OnFireCondition))]
    [InlineData("""{"type":"sheep_not_sheared"}""", typeof(SheepNotShearedCondition))]
    [InlineData("""{"type":"slime_size","size":1}""", typeof(SlimeSizeCondition))]
    public void Every_named_condition_parses(string json, Type expected)
    {
        Assert.IsType(expected, LootJson.ParseCondition(Json(json)));
    }

    [Fact]
    public void Loot_table_json_round_trips_pools_entries_and_weights()
    {
        LootTable table = LootJson.ParseTable(Json("""
        {
          "Pools": [
            { "Entries": [{"Item":"betasharp:arrow"}], "MinCount": 1, "MaxCount": 1 },
            { "Entries": [{"Item":"betasharp:bone"}], "MinCount": 1, "MaxCount": 1 }
          ]
        }
        """));

        List<int> ids = table.Roll(new LootContext(null, null, 0, System.Random.Shared)).Select(s => s.ItemId).ToList();

        Assert.Equal(2, ids.Count);
        Assert.Contains(Item.ByName("arrow").Id, ids);
        Assert.Contains(Item.ByName("bone").Id, ids);
    }

    [Fact]
    public void Loot_entry_can_name_a_block_as_well_as_an_item()
    {
        LootTable table = LootJson.ParseTable(Json("""
        { "Pools": [ { "Entries": [{"Item":"betasharp:wool"}], "MinCount": 1, "MaxCount": 1 } ] }
        """));

        ItemStack stack = Assert.Single(table.Roll(new LootContext(null, null, 0, System.Random.Shared)));
        Assert.Equal(BetaSharp.Blocks.BlockRegistry.Get("wool").id, stack.ItemId);
    }

    [Fact]
    public void Unknown_item_in_a_loot_entry_fails_when_rolled()
    {
        LootTable table = LootJson.ParseTable(Json("""
        { "Pools": [ { "Entries": [{"Item":"betasharp:not_a_real_item"}], "MinCount": 1, "MaxCount": 1 } ] }
        """));

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => table.Roll(new LootContext(null, null, 0, System.Random.Shared)).ToList());
        Assert.Contains("not_a_real_item", error.Message);
    }

    [Fact]
    public void Sheep_wool_takes_its_meta_from_the_live_fleece_colour()
    {
        FakeWorldContext world = new();
        EntitySheep sheep = new(world) { FleeceColor = 11 };

        LootTable table = LootJson.ParseTable(Json("""
        { "Pools": [ { "Entries": [{"Item":"betasharp:wool","MetaFrom":"FleeceColor"}], "MinCount": 1, "MaxCount": 1 } ] }
        """));

        ItemStack stack = Assert.Single(table.Roll(LootContext.ForMob(sheep, null)));
        Assert.Equal(11, stack.getDamage());
    }

    [Fact]
    public void Slots_are_wired_from_json_for_every_shipped_mob()
    {
        FakeWorldContext world = new();

        EntityZombie zombie = new(world);
        Assert.IsType<MeleeAttackBehavior>(zombie.Attack);
        Assert.IsType<AlwaysHuntTargetBehavior>(zombie.Targeting);
        Assert.IsType<LootTableBehavior>(zombie.Loot);

        EntitySpider spider = new(world);
        Assert.IsType<JumpAttackBehavior>(spider.Attack);
        Assert.IsType<DarknessOnlyTargetBehavior>(spider.Targeting);

        Assert.IsType<RangedAttackBehavior>(new EntitySkeleton(world).Attack);
        Assert.IsType<SlimeSplitBehavior>(new EntitySlime(world).Lifecycle);
        Assert.IsType<PigLightningBehavior>(new EntityPig(world).Lifecycle);

        // Animals declare no Attack/Targeting, and a wolf declares no Loot at all.
        EntityAnimal cow = (EntityAnimal)EntityRegistry.ByName("cow").Create(world);
        Assert.Null(cow.Attack);
        Assert.Null(cow.Targeting);
        Assert.Null(new EntityWolf(world).Loot);
    }

    [Fact]
    public void Declaring_an_attack_slot_on_a_non_creature_fails_loudly()
    {
        EntityDefinition definition = new()
        {
            ProtocolId = 55,
            Name = "test_slime",
            Behaviors = [Json("""{"Slots":["Attack"],"Type":"melee"}""")]
        };

        // Validated at load from the registered base type, not per spawn.
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => EntityFactory.BuildBehaviors(definition, typeof(EntitySlime)));
        Assert.Contains("EntityCreature", error.Message);
    }
}
