using System.Text.Json;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Entities.State;
using OmniBlock.Items;
using OmniBlock.Loot;
using OmniBlock.Loot.Conditions;

namespace OmniBlock.Tests.Entities;

/// <summary>
/// Covers behavior loading: capability slots are declared in <c>assets/entity/*.json</c> and built
/// at registration, not wired in mob constructors.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityBehaviorJsonTests
{
    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    /// <summary>Deserializes one behavior entry the way the entity loader does.</summary>
    private static EntityBehaviorDefinition Behavior(string json) =>
        JsonSerializer.Deserialize<EntityBehaviorDefinition>(json)!;

    /// <summary>Behaviors are now built with a load-time context, once per entity type.</summary>
    private static object Build(string json, EntityDefinition? definition = null) =>
        EntityBehaviorRegistry.Build(new EntityBehaviorContext(
            Json(json),
            definition ?? new EntityDefinition { ProtocolId = 1, Name = "test" },
            new EntityStateLayout(),
            ContentRuntime.Current.Items));

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
        EntityCreature spider = (EntityCreature)EntityRegistry.ByName("spider").Create(world);
        EntityCreature target = (EntityCreature)EntityRegistry.ByName("pig").Create(world);
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
    [InlineData("""{"type":"killed_by","entity":"skeleton"}""", typeof(KilledByCondition))]
    [InlineData("""{"type":"on_fire"}""", typeof(OnFireCondition))]
    [InlineData("""{"type":"sheep_not_sheared"}""", typeof(SheepNotShearedCondition))]
    [InlineData("""{"type":"size","size":1}""", typeof(SizeCondition))]
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
            { "Entries": [{"Item":"omniblock:arrow"}], "MinCount": 1, "MaxCount": 1 },
            { "Entries": [{"Item":"omniblock:bone"}], "MinCount": 1, "MaxCount": 1 }
          ]
        }
        """), ContentRuntime.Current.Items);

        List<int> ids = table.Roll(new LootContext(null, null, 0, System.Random.Shared)).Select(s => s.ItemId).ToList();

        Assert.Equal(2, ids.Count);
        Assert.Contains(ContentRuntime.Current.Items.Get("omniblock:arrow").Id, ids);
        Assert.Contains(ContentRuntime.Current.Items.Get("omniblock:bone").Id, ids);
    }

    [Fact]
    public void Loot_entry_can_name_a_block_as_well_as_an_item()
    {
        LootTable table = LootJson.ParseTable(Json("""
        { "Pools": [ { "Entries": [{"Item":"omniblock:wool"}], "MinCount": 1, "MaxCount": 1 } ] }
        """), ContentRuntime.Current.Items);

        ItemStack stack = Assert.Single(table.Roll(new LootContext(null, null, 0, System.Random.Shared)));
        Assert.Equal(OmniBlock.Blocks.BlockRegistry.Get("wool").Id, stack.ItemId);
    }

    [Fact]
    public void Unknown_item_in_a_loot_entry_fails_when_parsed()
    {
        KeyNotFoundException error = Assert.Throws<KeyNotFoundException>(() => LootJson.ParseTable(Json("""
            { "Pools": [ { "Entries": [{"Item":"omniblock:not_a_real_item"}], "MinCount": 1, "MaxCount": 1 } ] }
            """), ContentRuntime.Current.Items));
        Assert.Contains("not_a_real_item", error.Message);
    }

    [Fact]
    public void Sheep_wool_takes_its_meta_from_the_live_fleece_colour()
    {
        FakeWorldContext world = new();
        EntityCreature sheep = (EntityCreature)EntityRegistry.ByName("sheep").Create(world);
        ((WoolBehavior)EntityRegistry.ByName("sheep").Behaviors.Interactable!).SetColorOn(sheep, 11);

        LootTable table = LootJson.ParseTable(Json("""
        { "Pools": [ { "Entries": [{"Item":"omniblock:wool","MetaFrom":"FleeceColor"}], "MinCount": 1, "MaxCount": 1 } ] }
        """), ContentRuntime.Current.Items);

        ItemStack stack = Assert.Single(table.Roll(LootContext.ForMob(sheep, null)));
        Assert.Equal(11, stack.GetDamage());
    }

    [Fact]
    public void Slots_are_wired_from_json_for_every_shipped_mob()
    {
        FakeWorldContext world = new();

        EntityCreature zombie = (EntityCreature)EntityRegistry.ByName("zombie").Create(world);
        Assert.IsType<MeleeAttackBehavior>(zombie.Attack);
        Assert.IsType<AlwaysHuntTargetBehavior>(zombie.Targeting);
        Assert.IsType<LootTableBehavior>(zombie.Loot);

        EntityCreature spider = (EntityCreature)EntityRegistry.ByName("spider").Create(world);
        // The jump attack is wrapped: a spider in daylight loses interest before it attacks.
        Assert.IsType<JumpAttackBehavior>(Assert.IsType<LoseTargetInDaylightBehavior>(spider.Attack).Inner);
        Assert.IsType<DarknessOnlyTargetBehavior>(spider.Targeting);

        Assert.IsType<RangedAttackBehavior>(EntityRegistry.ByName("skeleton").Behaviors.Attack);
        Assert.NotNull(EntityRegistry.ByName("slime").Behaviors.Find<SplitOnDeathBehavior>());
        Assert.IsType<LightningConversionBehavior>(EntityRegistry.ByName("pig").Behaviors.Lifecycle);

        // Animals declare no Attack/Targeting, and a wolf declares no Loot at all.
        EntityCreature cow = (EntityCreature)EntityRegistry.ByName("cow").Create(world);
        Assert.Null(cow.Attack);
        Assert.Null(cow.Targeting);
        Assert.Null(EntityRegistry.ByName("wolf").Behaviors.Loot);
    }

    [Fact]
    public void Declaring_an_attack_slot_on_a_non_creature_fails_loudly()
    {
        EntityDefinition definition = new()
        {
            ProtocolId = 55,
            Name = "test_slime",
            Behaviors = [Behavior("""{"Slots":["Attack"],"Type":"melee"}""")]
        };

        // Validated at load from the registered base type, not per spawn.
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => EntityFactory.BuildBehaviors(definition, typeof(EntityLiving)));
        Assert.Contains("EntityCreature", error.Message);
    }

    /// <summary>
    /// A typed definition deserializes its snake_case parameters into C# properties, so the
    /// behavior's constructor receives values rather than a JsonElement to dig through.
    /// </summary>
    [Fact]
    public void A_typed_definition_reads_its_parameters_into_properties()
    {
        BoatDefinition boat = Assert.IsType<BoatDefinition>(Behavior("""
            {
              "Slots": ["Ticker"],
              "Type": "boat",
              "break_damage": 25,
              "wreckage": [{ "Item": "omniblock:planks", "Count": 3 }]
            }
            """));

        Assert.Equal(["Ticker"], boat.Slots);
        Assert.Equal(25, boat.BreakDamage);
        Assert.Equal("omniblock:planks", Assert.Single(boat.Wreckage).Item);
        Assert.Equal(3, boat.Wreckage[0].Count);
    }

    /// <summary>An omitted parameter takes the property initialiser, not a hand-written fallback.</summary>
    [Fact]
    public void An_omitted_parameter_falls_back_to_the_property_default()
    {
        ArrowDefinition arrow = Assert.IsType<ArrowDefinition>(Behavior("""{"Slots":["Ticker"],"Type":"arrow"}"""));

        Assert.Equal(4, arrow.Damage);
    }

    /// <summary>
    /// The failure that used to surface as a bare TypeInitializationException: a bad value now
    /// names the entity and the behavior that carried it.
    /// </summary>
    [Fact]
    public void A_bad_value_names_the_entity_and_the_behavior()
    {
        EntityDefinition definition = new()
        {
            ProtocolId = 55,
            Name = "test_boat",
            Behaviors = [Behavior("""
                {"Slots":["Ticker"],"Type":"boat","wreckage":[{"Item":"omniblock:not_a_real_item","Count":1}]}
                """)]
        };

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => EntityFactory.BuildBehaviors(definition, typeof(EntityObject)));

        Assert.Contains("test_boat", error.Message);
        Assert.Contains("boat", error.Message);
        Assert.Contains("not_a_real_item", error.Message);
    }

    [Fact]
    public void An_unknown_behavior_type_names_itself()
    {
        EntityDefinition definition = new()
        {
            ProtocolId = 55,
            Name = "test_entity",
            Behaviors = [Behavior("""{"Slots":["Ticker"],"Type":"no_such_behavior"}""")]
        };

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => EntityFactory.BuildBehaviors(definition, typeof(EntityObject)));

        Assert.Contains("no_such_behavior", error.Message);
        Assert.Contains("test_entity", error.Message);
    }
}
