using OmniBlock.Inventories;
using OmniBlock.Items;
using OmniBlock.Recipes;
using OmniBlock.Registries;
using OmniBlock.Registries.Data;
using OmniBlock.Screens;
using OmniBlock.Entities;

namespace OmniBlock.Tests.Recipes;

[Collection(RecipeCharacterizationCollection.Name)]
public sealed class RecipeCharacterizationTests : IDisposable
{
    private readonly RuntimeItemRegistry _items = ContentRuntime.Current.Items;

    public RecipeCharacterizationTests()
    {
        RecipesCrafting.Clear();
        RecipesSmelting.Recipes.Clear();
    }

    public void Dispose()
    {
        RecipesCrafting.Clear();
        RecipesSmelting.Recipes.Clear();
    }

    [Fact]
    public void Shipped_catalog_loads_every_named_definition_and_preserves_type_counts()
    {
        var definitions = new DataAssetLoader<RecipeDefinition>("recipe", LoadLocations.Assets, allowUnhandled: false);
        definitions.LoadFromPaths(null, null, null);
        Assert.False(definitions.HasErrors, definitions.FirstErrorMessage);
        IReadableRegistry<RecipeDefinition> catalog = definitions;

        ResourceLocation[] keys = catalog.Keys.OrderBy(key => key.ToString()).ToArray();
        Assert.Equal(160, keys.Length);
        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.All(keys, key => Assert.Equal("omniblock", key.Namespace.ToString()));
        Assert.Contains(ResourceLocation.Parse("omniblock:stick"), keys);
        Assert.Contains(ResourceLocation.Parse("omniblock:repeater"), keys);
        Assert.Contains(ResourceLocation.Parse("omniblock:smelt_iron_ingot"), keys);

        var shaped = new ShapedCraftingRegistry();
        var shapeless = new ShapelessCraftingRegistry();
        var smelting = new SmeltingCraftingRegistry();
        foreach (ResourceLocation key in keys)
        {
            RecipeDefinition definition = catalog.GetOrThrow(key);
            Assert.Equal(key.Path, definition.Name);
            Assert.Equal(key.Namespace, definition.Namespace);

            switch (definition.Type.ToLowerInvariant())
            {
                case ShapedCraftingRegistry.Name:
                    shaped.BuildRecipe(definition, _items);
                    break;
                case ShapelessCraftingRegistry.Name:
                    shapeless.BuildRecipe(definition, _items);
                    break;
                case SmeltingCraftingRegistry.Name:
                    smelting.BuildRecipe(definition, _items);
                    break;
                default:
                    Assert.Fail($"Unknown shipped recipe type '{definition.Type}' on '{key}'.");
                    break;
            }
        }

        Assert.Equal(119, shaped.Count);
        Assert.Equal(31, shapeless.Count);
        Assert.Equal(10, smelting.Count);
    }

    [Fact]
    public void Shaped_recipe_matches_offsets_mirroring_empty_slots_and_exact_metadata()
    {
        Item stick = _items.Get("omniblock:stick");
        Item coal = _items.Get("omniblock:coal");
        Item output = _items.Get("omniblock:torch");
        var recipe = new ShapedRecipes(2, 1,
            [new ItemStack(stick, 1, 2), new ItemStack(coal, 1, -1)],
            new ItemStack(output, 4));

        InventoryCrafting normal = Grid((1, 1, stick, 2), (2, 1, coal, 7));
        InventoryCrafting mirrored = Grid((0, 2, coal, 4), (1, 2, stick, 2));
        InventoryCrafting wrongMetadata = Grid((1, 1, stick, 3), (2, 1, coal, 7));
        InventoryCrafting extraItem = Grid((1, 1, stick, 2), (2, 1, coal, 7), (0, 0, stick, 2));

        Assert.True(recipe.Matches(normal));
        Assert.True(recipe.Matches(mirrored));
        Assert.False(recipe.Matches(wrongMetadata));
        Assert.False(recipe.Matches(extraItem));
        Assert.Equal(4, recipe.GetCraftingResult(normal).Count);
    }

    [Fact]
    public void Shapeless_recipe_is_order_independent_but_rejects_extra_items()
    {
        Item stick = _items.Get("omniblock:stick");
        Item coal = _items.Get("omniblock:coal");
        Item output = _items.Get("omniblock:torch");
        var recipe = new ShapelessRecipes(new ItemStack(output, 2),
            [new ItemStack(stick, 1, -1), new ItemStack(coal, 1, 1)]);

        Assert.True(recipe.Matches(Grid((2, 2, coal, 1), (0, 0, stick, 9))));
        Assert.False(recipe.Matches(Grid((2, 2, coal, 0), (0, 0, stick, 9))));
        Assert.False(recipe.Matches(Grid((2, 2, coal, 1), (0, 0, stick, 9), (1, 1, stick, 0))));
    }

    [Fact]
    public void Construction_resolves_legacy_block_aliases_and_metadata_results()
    {
        var shaped = new ShapedCraftingRegistry();
        shaped.BuildRecipe(Definition("legacy_alias", "shaped", result: "omniblock:Wool:4",
            pattern: ["#"], key: new() { ["#"] = "omniblock:Planks" }), _items);

        ItemStack? result = RecipesCrafting.Craft(Grid((0, 0, _items.Get("omniblock:planks"), 0)));
        Assert.NotNull(result);
        Assert.Same(_items.Get("omniblock:wool"), result.GetItem());
        Assert.Equal(4, result.GetDamage());
    }

    [Fact]
    public void Duplicate_recipe_id_and_equivalent_recipe_are_rejected()
    {
        var registry = new ShapedCraftingRegistry();
        RecipeDefinition first = Definition("same_id", "shaped", "omniblock:stick",
            ["#"], new() { ["#"] = "omniblock:coal" });
        registry.BuildRecipe(first, _items);

        Assert.Throws<DuplicateRecipeException>(() => registry.BuildRecipe(first, _items));

        RecipeDefinition equivalent = Definition("different_id", "shaped", "omniblock:stick",
            ["#"], new() { ["#"] = "omniblock:coal" });
        Assert.Throws<DuplicateRecipeException>(() => registry.BuildRecipe(equivalent, _items));
    }

    [Fact]
    public void Overlapping_smelting_inputs_are_rejected()
    {
        var registry = new SmeltingCraftingRegistry();
        registry.BuildRecipe(Definition("first", "smelting", "omniblock:ingot_iron", input: "omniblock:IronOre"), _items);

        Assert.Throws<OverlappingRecipeException>(() => registry.BuildRecipe(
            Definition("second", "smelting", "omniblock:ingot_gold", input: "omniblock:IronOre"), _items));
    }

    [Theory]
    [InlineData("omniblock:missing", "omniblock:stick")]
    [InlineData("omniblock:coal", "omniblock:missing")]
    public void Unknown_crafting_item_references_fail_construction(string ingredient, string result)
    {
        var registry = new ShapedCraftingRegistry();
        RecipeDefinition definition = Definition("invalid_reference", "shaped", result,
            ["#"], new() { ["#"] = ingredient });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => registry.BuildRecipe(definition, _items));
        Assert.Contains("invalid_reference", error.Message);
        Assert.Contains("missing", error.Message);
    }

    private InventoryCrafting Grid(params (int X, int Y, Item Item, int Meta)[] entries)
    {
        var inventory = new InventoryCrafting(new TestScreenHandler(), 3, 3);
        foreach ((int x, int y, Item item, int meta) in entries)
            inventory.SetStack(x + y * 3, new ItemStack(item, 1, meta));
        return inventory;
    }

    private static RecipeDefinition Definition(
        string name,
        string type,
        string result,
        string[]? pattern = null,
        Dictionary<string, string>? key = null,
        string? input = null) => new()
        {
            Namespace = Namespace.OmniBlock,
            Name = name,
            Type = type,
            Pattern = pattern,
            Key = key,
            Input = input,
            Result = new ResultRef { Id = result }
        };

    private sealed class TestScreenHandler : ScreenHandler
    {
        public override bool canUse(EntityPlayer entityPlayer) => true;
    }
}
