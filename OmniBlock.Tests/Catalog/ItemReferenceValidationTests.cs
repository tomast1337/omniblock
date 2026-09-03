using System.Text.Json;
using OmniBlock.Items;
using OmniBlock.Registries;

namespace OmniBlock.Tests.Catalog;

public sealed class ItemReferenceValidationTests
{
    public static IEnumerable<object[]> InvalidReferences()
    {
        yield return [Definition("bad_crafting_return", maxStackSize: 1, craftingReturn: 31990), "31990"];
        yield return [Definition("bad_repair", repairIngredients: ["example:missing_repair_item"]), "example:missing_repair_item"];
        yield return [Definition("bad_food_container", Behavior("""{"Type":"food","HealAmount":1,"ReturnItem":"example:missing_container"}""")), "example:missing_container"];
        yield return [Definition("bad_tool_material", Behavior("""{"Type":"tool","Material":"example:missing_tool_material","Kind":"pickaxe"}""")), "example:missing_tool_material"];
        yield return [Definition("bad_armor_material", Behavior("""{"Type":"armor","Material":"example:missing_armor_material","Slot":0}""")), "example:missing_armor_material"];
        yield return [Definition("bad_placed_block", Behavior("""{"Type":"place_block","PlacesBlock":"example:missing_block"}""")), "example:missing_block"];
        yield return [Definition("bad_entity", Behavior("""{"Type":"throwable","ProjectileType":"example:missing_entity"}""")), "example:missing_entity"];
        yield return [Definition("bad_texture", textureId: "example:missing_texture"), "example:missing_texture"];
        yield return [Definition("bad_behavior_texture", Behavior("""{"Type":"fishing_rod","Cast":"example:missing_cast_texture"}""")), "example:missing_cast_texture"];
        yield return [Definition("bad_provider", Behavior("""{"Type":"example:missing_provider"}""")), "example:missing_provider"];
    }

    [Theory]
    [MemberData(nameof(InvalidReferences))]
    public void Invalid_references_fail_before_publication_with_owner_and_reference(
        ItemDefinition definition,
        string badReference)
    {
        ContentRuntime? publishedBefore = ContentRuntime.TryGetCurrent(out ContentRuntime? current) ? current : null;
        ContentRuntimeBuilder builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddItemDefinition(definition);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains($"omniblock:{definition.Name}", error.Message);
        Assert.Contains(badReference, error.Message);
        if (publishedBefore is not null)
        {
            Assert.True(ContentRuntime.TryGetCurrent(out ContentRuntime? publishedAfter));
            Assert.Same(publishedBefore, publishedAfter);
        }
        Assert.False(ContentRuntime.Current.Items.TryGetByProtocolId(definition.ProtocolId, out _));
    }

    private static ItemDefinition Definition(
        string name,
        JsonElement behavior = default,
        int maxStackSize = 64,
        int? craftingReturn = null,
        string[]? repairIngredients = null,
        string textureId = "") => new()
    {
        Name = name,
        ProtocolId = 31991,
        MaxStackSize = maxStackSize,
        CraftingReturnItemProtocolId = craftingReturn,
        RepairIngredients = repairIngredients ?? [],
        TextureId = textureId,
        Behaviors = behavior.ValueKind == JsonValueKind.Undefined ? [] : [behavior]
    };

    private static JsonElement Behavior(string json) => JsonSerializer.Deserialize<JsonElement>(json);
}
