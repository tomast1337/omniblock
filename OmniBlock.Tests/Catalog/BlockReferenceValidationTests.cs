using System.Text.Json;
using OmniBlock.Blocks;

namespace OmniBlock.Tests.Catalog;

public sealed class BlockReferenceValidationTests
{
    public static IEnumerable<object[]> InvalidReferences()
    {
        yield return
        [
            Definition("bad_material") with
            {
                Material = "example:missing_material"
            },
            "example:missing_material"
        ];
        yield return
        [
            Definition("bad_texture") with
            {
                TextureId = "example:missing_texture"
            },
            "example:missing_texture"
        ];
        yield return
        [
            Definition("bad_sound") with
            {
                SoundGroup = "example:missing_sound"
            },
            "example:missing_sound"
        ];
        yield return
        [
            Definition("bad_loot") with
            {
                LootTable = new LootTableDefinition([new LootEntryDefinition("example:missing_item")])
            },
            "example:missing_item"
        ];
        yield return
        [
            Definition("bad_block_entity") with
            {
                TileEntity = "example:missing_entity"
            },
            "example:missing_entity"
        ];
        yield return
        [
            Definition("bad_block_reference") with
            {
                Behaviors = [BehaviorWithProperty("stairs", "Visuals", "\"base\":\"example:missing_block\"")]
            },
            "example:missing_block"
        ];
        yield return
        [
            Definition("bad_provider") with
            {
                Behaviors = [Behavior("example:missing_provider", "Ticker")]
            },
            "example:missing_provider"
        ];
        yield return
        [
            Definition("bad_slot") with
            {
                Behaviors = [Behavior("button", "NotASlot")]
            },
            "NotASlot"
        ];
        yield return
        [
            Definition("duplicate_slot") with
            {
                Behaviors = [Behavior("button", "Ticker", "Ticker")]
            },
            "Ticker"
        ];
    }

    [Theory]
    [MemberData(nameof(InvalidReferences))]
    public void Invalid_references_fail_at_build_with_owner_and_reference(
        BlockDefinition definition,
        string badReference)
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddBlockDefinition(definition);

        var error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains($"omniblock:{definition.Name}", error.Message);
        Assert.Contains(badReference, error.Message);
    }

    private static BlockDefinition Definition(string name) => new()
    {
        Name = name,
        ProtocolId = 240,
        Material = "stone"
    };

    private static JsonElement Behavior(string type, string slot, string? secondSlot = null)
    {
        var slots = secondSlot is null ? $"\"{slot}\"" : $"\"{slot}\",\"{secondSlot}\"";
        return JsonSerializer.Deserialize<JsonElement>($$"""{"Type":"{{type}}","Slots":[{{slots}}]}""");
    }

    private static JsonElement BehaviorWithProperty(string type, string slot, string additionalProperty) =>
        JsonSerializer.Deserialize<JsonElement>(
            $$"""{"Type":"{{type}}","Slots":["{{slot}}"],{{additionalProperty}}}""");
}
