using System.Text.Json;
using OmniBlock.Entities;
using OmniBlock.Inventories;
using OmniBlock.Items;
using OmniBlock.Processes;
using OmniBlock.Recipes;
using OmniBlock.Registries.Data;
using OmniBlock.Screens;

namespace OmniBlock.Tests.Recipes;

[Collection(RecipeCharacterizationCollection.Name)]
public sealed class RecipeCharacterizationTests
{
    private readonly RuntimeItemRegistry _items = ContentRuntime.Current.Items;

    [Fact]
    public void Shipped_catalog_loads_every_named_definition_and_preserves_type_counts()
    {
        var definitions = new DataAssetLoader<RecipeDefinition>("recipe", LoadLocations.Assets, false);
        definitions.LoadFromPaths(null, null, null);
        Assert.False(definitions.HasErrors, definitions.FirstErrorMessage);
        IReadableRegistry<RecipeDefinition> catalog = definitions;

        var keys = catalog.Keys.OrderBy(key => key.ToString()).ToArray();
        Assert.Equal(160, keys.Length);
        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.All(keys, key => Assert.Equal("omniblock", key.Namespace.ToString()));
        Assert.Contains(ResourceLocation.Parse("omniblock:stick"), keys);
        Assert.Contains(ResourceLocation.Parse("omniblock:repeater"), keys);
        Assert.Contains(ResourceLocation.Parse("omniblock:smelt_iron_ingot"), keys);

        foreach (var key in keys)
        {
            var definition = catalog.GetOrThrow(key);
            Assert.Equal(key.Path, definition.Name);
            Assert.Equal(key.Namespace, definition.Namespace);
        }

        var processes = ContentRuntime.Current.Processes;
        Assert.Equal(119, processes.GetByType(ProcessTypes.CraftingShaped).Count);
        Assert.Equal(31, processes.GetByType(ProcessTypes.CraftingShapeless).Count);
        Assert.Equal(10, processes.GetByType(ProcessTypes.Smelting).Count);
    }

    [Fact]
    public void Shaped_recipe_matches_offsets_mirroring_empty_slots_and_exact_metadata()
    {
        var stick = _items.Get("omniblock:stick");
        var coal = _items.Get("omniblock:coal");
        var output = _items.Get("omniblock:torch");
        var recipe = new ShapedRecipes(2, 1,
            [new ItemStack(stick, 1, 2), new ItemStack(coal, 1, -1)],
            new ItemStack(output, 4));

        var normal = Grid((1, 1, stick, 2), (2, 1, coal, 7));
        var mirrored = Grid((0, 2, coal, 4), (1, 2, stick, 2));
        var wrongMetadata = Grid((1, 1, stick, 3), (2, 1, coal, 7));
        var extraItem = Grid((1, 1, stick, 2), (2, 1, coal, 7), (0, 0, stick, 2));

        Assert.True(recipe.Matches(normal));
        Assert.True(recipe.Matches(mirrored));
        Assert.False(recipe.Matches(wrongMetadata));
        Assert.False(recipe.Matches(extraItem));
        Assert.Equal(4, recipe.GetCraftingResult(normal).Count);
    }

    [Fact]
    public void Shapeless_recipe_is_order_independent_but_rejects_extra_items()
    {
        var stick = _items.Get("omniblock:stick");
        var coal = _items.Get("omniblock:coal");
        var output = _items.Get("omniblock:torch");
        var recipe = new ShapelessRecipes(new ItemStack(output, 2),
            [new ItemStack(stick, 1, -1), new ItemStack(coal, 1, 1)]);

        Assert.True(recipe.Matches(Grid((2, 2, coal, 1), (0, 0, stick, 9))));
        Assert.False(recipe.Matches(Grid((2, 2, coal, 0), (0, 0, stick, 9))));
        Assert.False(recipe.Matches(Grid((2, 2, coal, 1), (0, 0, stick, 9), (1, 1, stick, 0))));
    }

    [Fact]
    public void Construction_resolves_legacy_block_aliases_and_metadata_results()
    {
        var runtime = ContentRuntime.Current.WithProcesses([
            Process("legacy_alias", "shaped", """
                                              {"pattern":["#"],"key":{"#":"omniblock:Planks"},
                                               "result":{"id":"omniblock:Wool:4","count":1}}
                                              """)
        ]);

        var result = runtime.Processes.Crafting.Craft(
            Grid((0, 0, runtime.Items.Get("omniblock:planks"), 0)));
        Assert.NotNull(result);
        Assert.Same(runtime.Items.Get("omniblock:wool"), result.GetItem());
        Assert.Equal(4, result.GetDamage());
    }

    [Theory]
    [InlineData("omniblock:missing", "omniblock:stick")]
    [InlineData("omniblock:coal", "omniblock:missing")]
    public void Unknown_crafting_item_references_fail_construction(string ingredient, string result)
    {
        var providerJson = "{\"pattern\":[\"#\"],\"key\":{\"#\":\"" + ingredient
                                                                    + "\"},\"result\":{\"id\":\"" + result + "\",\"count\":1}}";
        var definition = Process("invalid_reference", "shaped", providerJson);

        var error = Assert.Throws<InvalidOperationException>(() => ContentRuntime.Current.WithProcesses([definition]));
        Assert.Contains("invalid_reference", error.Message);
        Assert.Contains("missing", error.Message);
    }

    private InventoryCrafting Grid(params (int X, int Y, Item Item, int Meta)[] entries)
    {
        var inventory = new InventoryCrafting(new TestScreenHandler(), 3, 3);
        foreach (var (x, y, item, meta) in entries)
            inventory.SetStack(x + y * 3, new ItemStack(item, 1, meta));
        return inventory;
    }

    private static ProcessDefinition Process(string name, string type, string providerJson)
    {
        return new ProcessDefinition
        {
            DeclaredId = "omniblock:" + name,
            Type = type,
            ProviderData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(providerJson)!
        };
    }

    private sealed class TestScreenHandler : ScreenHandler
    {
        public override bool canUse(EntityPlayer entityPlayer) => true;
    }
}
