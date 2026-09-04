using System.Text.Json;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Entities.State;
using OmniBlock.Loot;
using OmniBlock.Loot.Conditions;

namespace OmniBlock.Tests.Entities;

/// <summary>
///     Covers behavior loading: capability slots are declared in <c>assets/entity/*.json</c> and built
///     at registration, not wired in mob constructors.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityBehaviorJsonTests
{
    private static JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    /// <summary>Deserializes one behavior entry the way the entity loader does.</summary>
    private static JsonElement Behavior(string json) => Json(json);

    /// <summary>Behaviors are now built with a load-time context, once per entity type.</summary>
    private static object Build(string json, EntityDefinition? definition = null)
    {
        var providers = new EntityBehaviorProviderRegistry();
        var context = new EntityBehaviorBuildContext(
            definition ?? new EntityDefinition
            {
                ProtocolId = 1,
                Name = "test"
            },
            new EntityStateLayout(),
            ContentRuntime.Current.Blocks,
            ContentRuntime.Current.Items,
            new RegistryEntityTypeView(),
            providers);
        return context.Build(Json(json));
    }

    private static EntityBehaviorSet BuildBehaviors(EntityDefinition definition, Type entityType)
    {
        var providers = new EntityBehaviorProviderRegistry();
        var dependencies = new EntityBuildContext(
            ContentRuntime.Current.Blocks, ContentRuntime.Current.Items, new RegistryEntityTypeView());
        return EntityFactory.BuildBehaviors(definition, entityType, dependencies, providers);
    }

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
        var built = Build("""{"Type":"jump","fallback":{"Type":"melee"}}""");
        var jump = Assert.IsType<JumpAttackBehavior>(built);

        // The nested melee is what a spider falls back to outside its lunge band.
        FakeWorldContext world = new();
        var spider = (EntityCreature)TestEntityCatalog.ByName("spider").Create(world);
        var target = (EntityCreature)TestEntityCatalog.ByName("pig").Create(world);
        spider.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        target.SetPositionAndAngles(9.0, 65.0, 8.5, 0f, 0f);
        var before = target.Health;

        jump.AttackEntity(spider, target, 1.0f); // inside melee range, outside the jump band
        Assert.True(target.Health < before);
    }

    [Fact]
    public void Unknown_behavior_type_fails_loudly()
    {
        var error = Assert.Throws<ArgumentException>(() => Build("""{"Type":"teleport"}"""));
        Assert.Contains("teleport", error.Message);
    }

    [Fact]
    public void Unknown_loot_condition_fails_loudly()
    {
        var error = Assert.Throws<ArgumentException>(() => LootJson.ParseCondition(Json("""{"type":"phase_of_the_moon"}""")));
        Assert.Contains("phase_of_the_moon", error.Message);
    }

    [Theory]
    [InlineData("""{"type":"killed_by","entity":"skeleton"}""", typeof(KilledByCondition))]
    [InlineData("""{"type":"on_fire"}""", typeof(OnFireCondition))]
    [InlineData("""{"type":"sheep_not_sheared"}""", typeof(SheepNotShearedCondition))]
    [InlineData("""{"type":"size","size":1}""", typeof(SizeCondition))]
    public void Every_named_condition_parses(string json, Type expected) => Assert.IsType(expected, LootJson.ParseCondition(Json(json)));

    [Fact]
    public void Loot_table_json_round_trips_pools_entries_and_weights()
    {
        var table = LootJson.ParseTable(Json("""
                                             {
                                               "Pools": [
                                                 { "Entries": [{"Item":"omniblock:arrow"}], "MinCount": 1, "MaxCount": 1 },
                                                 { "Entries": [{"Item":"omniblock:bone"}], "MinCount": 1, "MaxCount": 1 }
                                               ]
                                             }
                                             """), ContentRuntime.Current.Items);

        var ids = table.Roll(new LootContext(null, null, 0, Random.Shared)).Select(s => s.ItemId).ToList();

        Assert.Equal(2, ids.Count);
        Assert.Contains(ContentRuntime.Current.Items.Get("omniblock:arrow").Id, ids);
        Assert.Contains(ContentRuntime.Current.Items.Get("omniblock:bone").Id, ids);
    }

    [Fact]
    public void Loot_entry_can_name_a_block_as_well_as_an_item()
    {
        var table = LootJson.ParseTable(Json("""
                                             { "Pools": [ { "Entries": [{"Item":"omniblock:wool"}], "MinCount": 1, "MaxCount": 1 } ] }
                                             """), ContentRuntime.Current.Items);

        var stack = Assert.Single(table.Roll(new LootContext(null, null, 0, Random.Shared)));
        Assert.Equal(TestBlocks.Get("wool").Id, stack.ItemId);
    }

    [Fact]
    public void Unknown_item_in_a_loot_entry_fails_when_parsed()
    {
        var error = Assert.Throws<KeyNotFoundException>(() => LootJson.ParseTable(Json("""
                                                                                       { "Pools": [ { "Entries": [{"Item":"omniblock:not_a_real_item"}], "MinCount": 1, "MaxCount": 1 } ] }
                                                                                       """), ContentRuntime.Current.Items));
        Assert.Contains("not_a_real_item", error.Message);
    }

    [Fact]
    public void Sheep_wool_takes_its_meta_from_the_live_fleece_colour()
    {
        FakeWorldContext world = new();
        var sheep = (EntityCreature)TestEntityCatalog.ByName("sheep").Create(world);
        ((WoolBehavior)TestEntityCatalog.ByName("sheep").Behaviors.Interactable!).SetColorOn(sheep, 11);

        var table = LootJson.ParseTable(Json("""
                                             { "Pools": [ { "Entries": [{"Item":"omniblock:wool","MetaFrom":"FleeceColor"}], "MinCount": 1, "MaxCount": 1 } ] }
                                             """), ContentRuntime.Current.Items);

        var stack = Assert.Single(table.Roll(LootContext.ForMob(sheep, null)));
        Assert.Equal(11, stack.GetDamage());
    }

    [Fact]
    public void Slots_are_wired_from_json_for_every_shipped_mob()
    {
        FakeWorldContext world = new();

        var zombie = (EntityCreature)TestEntityCatalog.ByName("zombie").Create(world);
        Assert.IsType<MeleeAttackBehavior>(zombie.Attack);
        Assert.IsType<AlwaysHuntTargetBehavior>(zombie.Targeting);
        Assert.IsType<LootTableBehavior>(zombie.Loot);

        var spider = (EntityCreature)TestEntityCatalog.ByName("spider").Create(world);
        // The jump attack is wrapped: a spider in daylight loses interest before it attacks.
        Assert.IsType<JumpAttackBehavior>(Assert.IsType<LoseTargetInDaylightBehavior>(spider.Attack).Inner);
        Assert.IsType<DarknessOnlyTargetBehavior>(spider.Targeting);

        Assert.IsType<RangedAttackBehavior>(TestEntityCatalog.ByName("skeleton").Behaviors.Attack);
        Assert.NotNull(TestEntityCatalog.ByName("slime").Behaviors.Find<SplitOnDeathBehavior>());
        Assert.IsType<LightningConversionBehavior>(TestEntityCatalog.ByName("pig").Behaviors.Lifecycle);

        // Animals declare no Attack/Targeting, and a wolf declares no Loot at all.
        var cow = (EntityCreature)TestEntityCatalog.ByName("cow").Create(world);
        Assert.Null(cow.Attack);
        Assert.Null(cow.Targeting);
        Assert.Null(TestEntityCatalog.ByName("wolf").Behaviors.Loot);
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
        var error = Assert.Throws<ArgumentException>(() => BuildBehaviors(definition, typeof(EntityLiving)));
        Assert.Contains("EntityCreature", error.Message);
    }

    [Fact]
    public void Unknown_and_duplicate_behavior_slots_fail_loudly()
    {
        EntityDefinition unknown = new()
        {
            ProtocolId = 20,
            Name = "bad_slot",
            Behaviors = [Behavior("""{"Slots":["Teleport"],"Type":"ignore_fall_damage"}""")]
        };
        EntityDefinition duplicate = new()
        {
            ProtocolId = 21,
            Name = "duplicate_slot",
            Behaviors =
            [
                Behavior("""{"Slots":["Physics"],"Type":"ignore_fall_damage"}"""),
                Behavior("""{"Slots":["Physics"],"Type":"ignore_fall_damage"}""")
            ]
        };

        var unknownError = Assert.Throws<ArgumentException>(() => BuildBehaviors(unknown, typeof(EntityObject)));
        var duplicateError = Assert.Throws<ArgumentException>(() => BuildBehaviors(duplicate, typeof(EntityObject)));

        Assert.Contains("bad_slot", unknownError.Message);
        Assert.Contains("Teleport", unknownError.Message);
        Assert.Contains("duplicate_slot", duplicateError.Message);
        Assert.Contains("Physics", duplicateError.Message);
    }

    [Fact]
    public void A_behavior_without_slots_fails_loudly()
    {
        EntityDefinition definition = new()
        {
            ProtocolId = 20,
            Name = "slotless",
            Behaviors = [Behavior("""{"Slots":[],"Type":"ignore_fall_damage"}""")]
        };

        var error = Assert.Throws<ArgumentException>(() => BuildBehaviors(definition, typeof(EntityObject)));

        Assert.Contains("slotless", error.Message);
        Assert.Contains("no slots", error.Message);
    }

    /// <summary>
    ///     A typed definition deserializes its snake_case parameters into C# properties, so the
    ///     behavior's constructor receives values rather than a JsonElement to dig through.
    /// </summary>
    [Fact]
    public void A_typed_definition_reads_its_parameters_into_properties()
    {
        var boat = Behavior("""
                            {
                              "Slots": ["Ticker"],
                              "Type": "boat",
                              "break_damage": 25,
                              "wreckage": [{ "Item": "omniblock:planks", "Count": 3 }]
                            }
                            """).Deserialize<BoatDefinition>(ProviderJsonOptions())!;

        Assert.Equal(["Ticker"], boat.Slots);
        Assert.Equal(25, boat.BreakDamage);
        Assert.Equal("omniblock:planks", Assert.Single(boat.Wreckage).Item);
        Assert.Equal(3, boat.Wreckage[0].Count);
    }

    /// <summary>An omitted parameter takes the property initialiser, not a hand-written fallback.</summary>
    [Fact]
    public void An_omitted_parameter_falls_back_to_the_property_default()
    {
        var arrow = Behavior("""{"Slots":["Ticker"],"Type":"arrow"}""").Deserialize<ArrowDefinition>(ProviderJsonOptions())!;

        Assert.Equal(4, arrow.Damage);
    }

    /// <summary>
    ///     The failure that used to surface as a bare TypeInitializationException: a bad value now
    ///     names the entity and the behavior that carried it.
    /// </summary>
    [Fact]
    public void A_bad_value_names_the_entity_and_the_behavior()
    {
        EntityDefinition definition = new()
        {
            ProtocolId = 55,
            Name = "test_boat",
            Behaviors =
            [
                Behavior("""
                         {"Slots":["Ticker"],"Type":"boat","wreckage":[{"Item":"omniblock:not_a_real_item","Count":1}]}
                         """)
            ]
        };

        var error = Assert.Throws<ArgumentException>(() => BuildBehaviors(definition, typeof(EntityObject)));

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

        var error = Assert.Throws<ArgumentException>(() => BuildBehaviors(definition, typeof(EntityObject)));

        Assert.Contains("no_such_behavior", error.Message);
        Assert.Contains("test_entity", error.Message);
    }

    private static JsonSerializerOptions ProviderJsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private sealed class RegistryEntityTypeView : IEntityTypeBuildView
    {
        public EntityType Get(ResourceLocation key) =>
            ContentRuntime.Current.EntityTypes.Get(key);

        public bool TryGet(ResourceLocation key, out EntityType? type) =>
            ContentRuntime.Current.EntityTypes.TryGet(key, out type);
    }
}
