using System.Text.Json;
using OmniBlock.Entities;
using OmniBlock.Registries;

namespace OmniBlock.Tests.Entities;

[Collection("EntityTests")]
public sealed class EntityReferenceValidationTests
{
    [Fact]
    public void Unknown_held_item_identifies_the_owning_entity_and_item()
    {
        EntityDefinition definition = Definition("bad_held_item", 20);
        definition = definition with { HeldItem = "example:missing_item" };

        AssertInvalid(definition, "omniblock:bad_held_item", "example:missing_item");
    }

    [Theory]
    [InlineData("bad_loot", """{"Type":"loot_table","Slots":["Loot"],"Pools":[{"Entries":[{"Item":"example:missing_item"}]}]}""", "omniblock:loot_table", "example:missing_item")]
    [InlineData("bad_block", """{"Type":"settle_as_block","Slots":["Ticker"],"wire_ids":[{"Block":"example:missing_block","Id":70}]}""", "omniblock:settle_as_block", "example:missing_block")]
    [InlineData("bad_hatch", """{"Type":"thrown_projectile","Slots":["Ticker"],"hatch":{"Entity":"example:missing_hatchling"}}""", "omniblock:thrown_projectile", "example:missing_hatchling")]
    [InlineData("bad_rider", """{"Type":"spawn_rider","Slots":["Lifecycle"],"rider":"example:missing_rider"}""", "omniblock:spawn_rider", "example:missing_rider")]
    [InlineData("bad_conversion", """{"Type":"lightning_conversion","Slots":["Lifecycle"],"becomes":"example:missing_conversion"}""", "omniblock:lightning_conversion", "example:missing_conversion")]
    [InlineData("bad_provider", """{"Type":"example:missing_provider","Slots":["Physics"]}""", "example:missing_provider", "Unknown entity behavior provider")]
    public void Behavior_reference_failures_identify_owner_provider_and_reference(
        string owner,
        string behavior,
        string provider,
        string reference)
    {
        AssertInvalid(Definition(owner, 20, behavior), $"omniblock:{owner}", provider, reference);
    }

    [Fact]
    public void Unknown_constructor_provider_identifies_owner_and_provider()
    {
        EntityDefinition definition = Definition("bad_constructor", 20) with
        {
            Constructor = "example:missing_constructor"
        };

        AssertInvalid(definition, "omniblock:bad_constructor", "example:missing_constructor");
    }

    [Fact]
    public void Missing_synced_property_reference_fails_during_behavior_compilation()
    {
        AssertInvalid(
            Definition("bad_synced", 20,
                """{"Type":"fuse","Slots":["Attack","Ticker","Lifecycle"]}"""),
            "omniblock:bad_synced",
            "omniblock:fuse",
            "state");
    }

    [Fact]
    public void Duplicate_synced_wire_ids_fail_before_publication()
    {
        EntityDefinition definition = Definition("duplicate_synced", 20) with
        {
            SyncedProperties =
            [
                new("first", 16, OmniBlock.Entities.State.SyncedValueKind.Byte),
                new("second", 16, OmniBlock.Entities.State.SyncedValueKind.Int)
            ]
        };

        AssertInvalid(definition, "omniblock:duplicate_synced", "16");
    }

    private static void AssertInvalid(EntityDefinition definition, params string[] fragments)
    {
        ContentRuntime published = ContentRuntime.Current;
        ContentRuntimeBuilder builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddEntityDefinition(definition);

        Exception error = Assert.ThrowsAny<Exception>(() => builder.Build());

        foreach (string fragment in fragments) Assert.Contains(fragment, error.ToString());
        Assert.Same(published, ContentRuntime.Current);
    }

    private static EntityDefinition Definition(string name, int protocolId, params string[] behaviors) => new()
    {
        Name = name,
        Namespace = Namespace.OmniBlock,
        ProtocolId = protocolId,
        Behaviors = [.. behaviors.Select(json => JsonSerializer.Deserialize<JsonElement>(json))]
    };
}
