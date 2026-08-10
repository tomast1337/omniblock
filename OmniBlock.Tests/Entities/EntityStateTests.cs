using System.IO;
using OmniBlock.Entities;
using OmniBlock.Entities.State;
using OmniBlock.Util;

namespace OmniBlock.Tests.Entities;

/// <summary>
/// Covers the per-entity state foundation that lets behaviors be shared per entity type instead of
/// held as fields on a subclass: typed <see cref="StateHandle{T}"/> slots for unsynced state, and
/// JSON-declared synced properties whose wire ids come from data.
/// </summary>
public sealed class EntityStateTests
{
    [Fact]
    public void Handles_round_trip_each_primitive_kind_independently()
    {
        EntityStateLayout layout = new();
        StateHandle<int> ticks = layout.DeclareInt();
        StateHandle<float> tilt = layout.DeclareFloat();
        StateHandle<double> waypoint = layout.DeclareDouble();
        StateHandle<bool> shaking = layout.DeclareBool();

        EntityState state = layout.Create();
        state[ticks] = 30;
        state[tilt] = 0.25F;
        state[waypoint] = -12.5D;
        state[shaking] = true;

        Assert.Equal(30, state[ticks]);
        Assert.Equal(0.25F, state[tilt]);
        Assert.Equal(-12.5D, state[waypoint]);
        Assert.True(state[shaking]);
    }

    [Fact]
    public void Declared_defaults_apply_to_every_new_instance()
    {
        EntityStateLayout layout = new();
        StateHandle<int> fuse = layout.DeclareInt(30);
        StateHandle<bool> armed = layout.DeclareBool(true);

        foreach (EntityState state in new[] { layout.Create(), layout.Create() })
        {
            Assert.Equal(30, state[fuse]);
            Assert.True(state[armed]);
        }
    }

    [Fact]
    public void Instances_from_one_layout_do_not_share_storage()
    {
        EntityStateLayout layout = new();
        StateHandle<int> counter = layout.DeclareInt();

        EntityState first = layout.Create();
        EntityState second = layout.Create();
        first[counter] = 7;

        // This is what makes a shared, stateless behavior safe across many entities.
        Assert.Equal(7, first[counter]);
        Assert.Equal(0, second[counter]);
    }

    [Fact]
    public void Reference_slots_keep_their_declared_type()
    {
        EntityStateLayout layout = new();
        StateHandle<string> owner = layout.DeclareRef<string>();

        EntityState state = layout.Create();
        Assert.Null(state.GetRef(owner));

        state.SetRef(owner, "tester");
        Assert.Equal("tester", state.GetRef(owner));
    }

    [Fact]
    public void Declared_synced_properties_reach_the_synchronizer()
    {
        DataSynchronizer sync = new();
        sync.MakeProperty<byte>(0, 0); // the shared flags byte every entity carries

        SyncedPropertyFactory.Declare(sync, [
            new SyncedPropertyDefinition("state", 16, SyncedValueKind.Byte, 255),
            new SyncedPropertyDefinition("powered", 17, SyncedValueKind.Bool)
        ], "creeper");

        Assert.Equal((byte)255, sync.Get<byte>(16).Value);
        Assert.False(sync.Get<bool>(17).Value);
    }

    [Fact]
    public void A_json_declared_property_serialises_identically_to_a_hardcoded_one()
    {
        // The wire header is (type << 5) | id, so a declared property must be byte-identical to the
        // hand-written MakeProperty call it replaces, or the client desynchronises.
        DataSynchronizer hardcoded = new();
        hardcoded.MakeProperty(17, true);

        DataSynchronizer declared = new();
        SyncedPropertyFactory.Declare(declared, [new SyncedPropertyDefinition("powered", 17, SyncedValueKind.Bool, 1)], "creeper");

        Assert.Equal(Serialize(hardcoded), Serialize(declared));
    }

    [Fact]
    public void Reserved_and_out_of_range_ids_fail_loudly()
    {
        DataSynchronizer sync = new();

        ArgumentException flags = Assert.Throws<ArgumentException>(() => SyncedPropertyFactory.Declare(
            sync, [new SyncedPropertyDefinition("bad", 0, SyncedValueKind.Bool)], "test"));
        Assert.Contains("reserved", flags.Message);

        ArgumentException tooBig = Assert.Throws<ArgumentException>(() => SyncedPropertyFactory.Declare(
            sync, [new SyncedPropertyDefinition("bad", 32, SyncedValueKind.Bool)], "test"));
        Assert.Contains("32", tooBig.Message);
    }

    [Fact]
    public void Resolving_a_handle_checks_the_declared_kind()
    {
        EntityDefinition definition = new()
        {
            ProtocolId = 50,
            Name = "creeper",
            SyncedProperties = [new SyncedPropertyDefinition("powered", 17, SyncedValueKind.Bool)]
        };

        Assert.Equal(17, SyncedPropertyFactory.Resolve<bool>(definition, "powered").Id);

        // A behavior asking for the wrong type is a load-time failure, not a runtime cast error.
        ArgumentException wrongType = Assert.Throws<ArgumentException>(
            () => SyncedPropertyFactory.Resolve<int>(definition, "powered"));
        Assert.Contains("Bool", wrongType.Message);

        ArgumentException missing = Assert.Throws<ArgumentException>(
            () => SyncedPropertyFactory.Resolve<bool>(definition, "nope"));
        Assert.Contains("nope", missing.Message);
    }

    private static byte[] Serialize(DataSynchronizer synchronizer)
    {
        using MemoryStream stream = new();
        synchronizer.WriteAll(stream);
        return stream.ToArray();
    }
}
