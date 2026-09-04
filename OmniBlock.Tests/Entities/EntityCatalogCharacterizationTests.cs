using OmniBlock.Entities;
using OmniBlock.Entities.State;
using OmniBlock.Registries;

namespace OmniBlock.Tests.Entities;

/// <summary>
/// Public catalog contract to preserve while entity ownership moves into ContentRuntime.
/// It intentionally observes registered values and construction results, not registry internals.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityCatalogCharacterizationTests
{
    private static readonly (string Name, int ProtocolId, int ObjectId, int GlobalId, Type RuntimeType)[] s_catalog =
    [
        ("item", 1, 0, 0, typeof(EntityObject)),
        ("painting", 9, 0, 0, typeof(EntityObject)),
        ("arrow", 10, 60, 0, typeof(EntityObject)),
        ("snowball", 11, 61, 0, typeof(EntityObject)),
        ("primedtnt", 20, 50, 0, typeof(EntityObject)),
        ("fallingsand", 21, 0, 0, typeof(EntityObject)),
        ("minecart", 40, 0, 0, typeof(EntityObject)),
        ("boat", 41, 1, 0, typeof(EntityObject)),
        ("creeper", 50, 0, 0, typeof(EntityCreature)),
        ("skeleton", 51, 0, 0, typeof(EntityCreature)),
        ("spider", 52, 0, 0, typeof(EntityCreature)),
        ("giant", 53, 0, 0, typeof(EntityCreature)),
        ("zombie", 54, 0, 0, typeof(EntityCreature)),
        ("slime", 55, 0, 0, typeof(EntityLiving)),
        ("ghast", 56, 0, 0, typeof(EntityLiving)),
        ("pigzombie", 57, 0, 0, typeof(EntityCreature)),
        ("egg", 62, 62, 0, typeof(EntityObject)),
        ("fireball", 63, 63, 0, typeof(EntityObject)),
        ("fishhook", 64, 90, 0, typeof(EntityObject)),
        ("lightningbolt", 65, 0, 1, typeof(EntityObject)),
        ("pig", 90, 0, 0, typeof(EntityCreature)),
        ("sheep", 91, 0, 0, typeof(EntityCreature)),
        ("cow", 92, 0, 0, typeof(EntityCreature)),
        ("chicken", 93, 0, 0, typeof(EntityCreature)),
        ("squid", 94, 0, 0, typeof(EntityLiving)),
        ("wolf", 95, 0, 0, typeof(EntityCreature)),
        ("player", 100, 0, 0, typeof(ServerPlayerEntity))
    ];

    [Fact]
    public void Shipped_catalog_keeps_names_ids_and_runtime_categories()
    {
        Assert.Equal(s_catalog.Select(row => row.Name),
            ContentRuntime.Current.EntityTypes.Keys.Select(key => key.Path).OrderBy(NameOrder));

        FakeWorldContext world = new();
        foreach ((string name, int protocolId, int objectId, int globalId, Type runtimeType) in s_catalog)
        {
            EntityType type = TestEntityCatalog.ByName(name);
            Assert.Equal(protocolId, ContentRuntime.Current.EntityTypes.GetProtocolId(type));
            Assert.Equal(runtimeType, type.BaseType);

            if (name != "player") Assert.Equal(runtimeType, type.Create(world).GetType());
            if (objectId != 0) Assert.Same(type, TestEntityCatalog.BySpawnObjectId(objectId));
            if (globalId != 0) Assert.Same(type, TestEntityCatalog.ByGlobalSpawnId(globalId));
        }
    }

    [Fact]
    public void Every_non_player_definition_keeps_core_physical_and_tracking_configuration()
    {
        foreach ((string name, _, _, _, _) in s_catalog.Where(row => row.Name != "player"))
        {
            EntityDefinition definition = TestEntityCatalog.ByName(name).RequireDefinition();
            Assert.True(definition.Width > 0, name);
            Assert.True(definition.Height > 0, name);
            Assert.True(definition.Health > 0, name);
            Assert.True(definition.TrackingRange >= 0, name);
            Assert.True(definition.TrackingFrequency > 0, name);
            Assert.Equal(name, definition.Name);
        }

        AssertDefinition("boat", 1.5f, 0.6f, tracksVelocity: true, collidable: true);
        AssertDefinition("arrow", 0.5f, 0.5f, tracksVelocity: true, collidable: false);
        AssertDefinition("ghast", 4f, 4f, tracksVelocity: false, collidable: false);
        Assert.True(TestEntityCatalog.ByName("ghast").RequireDefinition().FireImmune);
        Assert.True(TestEntityCatalog.ByName("squid").RequireDefinition().BreathesUnderwater);
        Assert.True(TestEntityCatalog.ByName("fishhook").RequireDefinition().IgnoreFrustumCheck);
        Assert.True(TestEntityCatalog.ByName("arrow").RequireDefinition().AlwaysSyncsRotation);
    }

    [Fact]
    public void Shipped_synced_property_layouts_keep_wire_ids_kinds_and_defaults()
    {
        AssertProperties("creeper", ("state", 16, SyncedValueKind.Byte, 255, null), ("powered", 17, SyncedValueKind.Bool, 0, null));
        AssertProperties("pig", ("saddled", 16, SyncedValueKind.Bool, 0, null));
        AssertProperties("sheep", ("wool", 16, SyncedValueKind.Byte, 0, null));
        AssertProperties("slime", ("size", 16, SyncedValueKind.Byte, 1, null));
        AssertProperties("ghast", ("charging", 16, SyncedValueKind.Bool, 0, null));
        AssertProperties("wolf", ("flags", 16, SyncedValueKind.Byte, 0, null),
            ("owner", 17, SyncedValueKind.String, 0, ""),
            ("shown_health", 18, SyncedValueKind.Int, 8, null));
    }

    [Fact]
    public void Every_declared_behavior_slot_is_attached_to_the_compiled_type()
    {
        foreach ((string name, _, _, _, _) in s_catalog.Where(row => row.Name != "player"))
        {
            EntityType type = TestEntityCatalog.ByName(name);
            foreach (string slot in type.RequireDefinition().Behaviors
                         .SelectMany(entry => entry.GetProperty("Slots").EnumerateArray())
                         .Select(entry => entry.GetString()!).Distinct())
                Assert.NotNull(Slot(type.Behaviors, slot));
        }
    }

    private static int NameOrder(string name) => Array.FindIndex(s_catalog, row => row.Name == name);

    private static void AssertDefinition(string name, float width, float height, bool tracksVelocity, bool collidable)
    {
        EntityDefinition definition = TestEntityCatalog.ByName(name).RequireDefinition();
        Assert.Equal(width, definition.Width);
        Assert.Equal(height, definition.Height);
        Assert.Equal(tracksVelocity, definition.TracksVelocity);
        Assert.Equal(collidable, definition.Collidable);
    }

    private static void AssertProperties(
        string name,
        params (string Name, int Id, SyncedValueKind Kind, double Default, string? DefaultString)[] expected)
    {
        SyncedPropertyDefinition[] actual = TestEntityCatalog.ByName(name).RequireDefinition().SyncedProperties;
        Assert.Equal(expected.Length, actual.Length);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Name, actual[i].Name);
            Assert.Equal(expected[i].Id, actual[i].Id);
            Assert.Equal(expected[i].Kind, actual[i].Kind);
            Assert.Equal(expected[i].Default, actual[i].Default);
            Assert.Equal(expected[i].DefaultString, actual[i].DefaultString);
        }
    }

    private static object? Slot(EntityBehaviorSet set, string slot) => slot switch
    {
        "Ticker" => set.Ticker,
        "Attack" => set.Attack,
        "Targeting" => set.Targeting,
        "Loot" => set.Loot,
        "Lifecycle" => set.Lifecycle,
        "Persistence" => set.Persistence,
        "Interactable" => set.Interactable,
        "Physics" => set.Physics,
        _ => throw new Xunit.Sdk.XunitException($"Unknown shipped entity behavior slot '{slot}'.")
    };
}
