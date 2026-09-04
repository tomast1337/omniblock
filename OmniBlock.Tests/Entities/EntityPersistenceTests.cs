using OmniBlock.Entities;
using OmniBlock.Entities.State;
using OmniBlock.NBT;
using OmniBlock.Util;

namespace OmniBlock.Tests.Entities;

/// <summary>
/// Covers the Persistence capability slot and its data-driven shortcut: a synced property that
/// names an <c>Nbt</c> key in the entity's JSON round-trips with no persistence code at all.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityPersistenceTests
{
    [Fact]
    public void Creeper_powered_survives_a_save_load_round_trip()
    {
        FakeWorldContext world = new();
        EntityCreature creeper = (EntityCreature)TestEntityCatalog.ByName("creeper").Create(world);
        creeper.Synced<bool>("powered")!.Value = true;
        creeper.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);

        NBTTagCompound nbt = new();
        Assert.True(creeper.SaveSelfNbt(nbt));

        EntityCreature loaded = Assert.IsType<EntityCreature>(TestEntityCatalog.GetEntityFromNbt(nbt, new FakeWorldContext()));
        Assert.True(loaded.Synced<bool>("powered")!.Value);
    }

    [Fact]
    public void Creeper_that_was_never_struck_loads_unpowered()
    {
        FakeWorldContext world = new();
        EntityCreature creeper = (EntityCreature)TestEntityCatalog.ByName("creeper").Create(world);
        creeper.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);

        NBTTagCompound nbt = new();
        Assert.True(creeper.SaveSelfNbt(nbt));

        EntityCreature loaded = Assert.IsType<EntityCreature>(TestEntityCatalog.GetEntityFromNbt(nbt, new FakeWorldContext()));
        Assert.False(loaded.Synced<bool>("powered")!.Value);
    }

    [Fact]
    public void Pig_saddle_survives_a_save_load_round_trip()
    {
        FakeWorldContext world = new();
        EntityCreature pig = (EntityCreature)TestEntityCatalog.ByName("pig").Create(world);
        pig.Synced<bool>("saddled")!.Value = true;
        pig.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);

        NBTTagCompound nbt = new();
        Assert.True(pig.SaveSelfNbt(nbt));

        EntityCreature loaded = Assert.IsType<EntityCreature>(TestEntityCatalog.GetEntityFromNbt(nbt, new FakeWorldContext()));
        Assert.True(loaded.Synced<bool>("saddled")!.Value);
    }

    [Fact]
    public void Declared_nbt_keys_keep_their_original_names()
    {
        // The save format is a compatibility surface: these keys must match what the hand-written
        // WriteNbt overrides used, or existing worlds silently lose state.
        FakeWorldContext world = new();
        EntityCreature creeper = (EntityCreature)TestEntityCatalog.ByName("creeper").Create(world);
        creeper.Synced<bool>("powered")!.Value = true;
        EntityCreature pig = (EntityCreature)TestEntityCatalog.ByName("pig").Create(world);
        pig.Synced<bool>("saddled")!.Value = true;

        NBTTagCompound creeperNbt = new();
        NBTTagCompound pigNbt = new();
        creeper.Write(creeperNbt);
        pig.Write(pigNbt);

        Assert.True(creeperNbt.GetBoolean("powered"));
        Assert.True(pigNbt.GetBoolean("Saddle"));
    }

    [Fact]
    public void Only_properties_naming_an_nbt_key_are_persisted()
    {
        DataSynchronizer sync = new(ContentRuntime.Current.Items);
        SyncedPropertyDefinition[] declarations =
        [
            new("saved", 16, SyncedValueKind.Int, 7, Nbt: "Saved"),
            new("transient", 17, SyncedValueKind.Int, 9)
        ];

        SyncedPropertyFactory.Declare(sync, declarations, "test");
        NBTTagCompound nbt = new();
        SyncedPropertyFactory.Write(sync, declarations, nbt);

        Assert.True(nbt.HasKey("Saved"));
        Assert.False(nbt.HasKey("transient"));
    }

    [Theory]
    [InlineData(SyncedValueKind.Byte)]
    [InlineData(SyncedValueKind.Short)]
    [InlineData(SyncedValueKind.Int)]
    [InlineData(SyncedValueKind.Float)]
    public void Every_numeric_kind_round_trips_through_nbt(SyncedValueKind kind)
    {
        SyncedPropertyDefinition[] declarations = [new("value", 16, kind, 12, Nbt: "Value")];

        DataSynchronizer saved = new(ContentRuntime.Current.Items);
        SyncedPropertyFactory.Declare(saved, declarations, "test");
        NBTTagCompound nbt = new();
        SyncedPropertyFactory.Write(saved, declarations, nbt);

        DataSynchronizer loaded = new(ContentRuntime.Current.Items);
        SyncedPropertyFactory.Declare(loaded, [declarations[0] with { Default = 0 }], "test");
        SyncedPropertyFactory.Read(loaded, declarations, nbt);

        object value = kind switch
        {
            SyncedValueKind.Byte => loaded.Get<byte>(16).Value,
            SyncedValueKind.Short => loaded.Get<short>(16).Value,
            SyncedValueKind.Int => loaded.Get<int>(16).Value,
            _ => loaded.Get<float>(16).Value
        };

        Assert.Equal(12, Convert.ToInt32(value));
    }

    [Fact]
    public void String_properties_round_trip_including_the_empty_default()
    {
        SyncedPropertyDefinition[] declarations = [new("owner", 17, SyncedValueKind.String, DefaultString: "tester", Nbt: "Owner")];

        DataSynchronizer saved = new(ContentRuntime.Current.Items);
        SyncedPropertyFactory.Declare(saved, declarations, "test");
        NBTTagCompound nbt = new();
        SyncedPropertyFactory.Write(saved, declarations, nbt);

        DataSynchronizer loaded = new(ContentRuntime.Current.Items);
        SyncedPropertyFactory.Declare(loaded, declarations, "test");
        SyncedPropertyFactory.Read(loaded, declarations, nbt);

        Assert.Equal("tester", loaded.Get<string?>(17).Value);
    }
}
