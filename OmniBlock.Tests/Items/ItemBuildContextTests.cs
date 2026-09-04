using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Entities;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Items.Behaviors;
using OmniBlock.Recipes;

namespace OmniBlock.Tests.Items;

public sealed class ItemBuildContextTests
{
    [Fact]
    public void Context_routes_every_dependency_through_injected_resolvers()
    {
        Block block = BlockRegistry.Get("stone");
        Item item = ContentRuntime.Current.Items.Get("omniblock:stick");
        ToolMaterial tool = ToolMaterialRegistry.Get("iron");
        ArmorMaterial armor = ArmorMaterialRegistry.Get("diamond");
        Material material = MaterialRegistry.Get("wood");
        EntityType entity = new((_, _) => null!, typeof(Entity), "test");
        BlockEntityType blockEntity = new(static () => new GenericBlockEntity(), "test");
        RecipeDefinition recipe = new();
        object interaction = new();
        List<string> calls = [];
        ItemBuildContext context = new(
            key => { calls.Add($"block:{key}"); return block; },
            key => { calls.Add($"block_item:{key}"); return item; },
            key => { calls.Add($"item:{key}"); return item; },
            key => { calls.Add($"tool:{key}"); return tool; },
            key => { calls.Add($"armor:{key}"); return armor; },
            key => { calls.Add($"material:{key}"); return material; },
            key => { calls.Add($"texture:{key}"); return 17; },
            key => { calls.Add($"entity:{key}"); return entity; },
            key => { calls.Add($"block_entity:{key}"); return blockEntity; },
            key => { calls.Add($"recipe:{key}"); return recipe; },
            key => { calls.Add($"interaction:{key}"); return interaction; });

        Assert.Same(block, context.ResolveBlock("example:block"));
        Assert.Same(item, context.ResolveBlockItem("example:block_item"));
        Assert.Same(item, context.ResolveItem("example:item"));
        Assert.Same(tool, context.ResolveToolMaterial("example:tool"));
        Assert.Same(armor, context.ResolveArmorMaterial("example:armor"));
        Assert.Same(material, context.ResolveBlockMaterial("example:material"));
        Assert.Equal(17, context.ResolveItemTexture("example:texture"));
        Assert.Same(entity, context.ResolveEntityType("example:entity"));
        Assert.Same(blockEntity, context.ResolveBlockEntityType("example:block_entity"));
        Assert.Same(recipe, context.ResolveRecipeDependency("example:recipe"));
        Assert.Same(interaction, context.ResolveInteractionDependency("example:interaction"));
        Assert.Equal(
            [
                "block:example:block", "block_item:example:block_item", "item:example:item",
                "tool:example:tool", "armor:example:armor", "material:example:material",
                "texture:example:texture", "entity:example:entity", "block_entity:example:block_entity",
                "recipe:example:recipe", "interaction:example:interaction"
            ], calls);
    }

    [Fact]
    public void Item_factory_uses_injected_texture_and_behavior_providers()
    {
        List<string> calls = [];
        ItemBuildContext context = Context(
            texture: key => { calls.Add($"texture:{key}"); return 91; });
        var providers = new RecordingProvider(calls);
        ItemDefinition definition = new()
        {
            Name = "injected",
            ProtocolId = 31999,
            TextureId = "example:icon",
            Behaviors = [Behavior("""{"Type":"shears"}""")]
        };

        Item item = ItemFactory.Create(definition, context, providers);

        Assert.Equal(91, item.GetTextureId(0));
        Assert.IsType<ShearsBehavior>(item.GetBehavior<IItemBehavior>());
        Assert.Equal(["texture:example:icon", "type:shears"], calls);
    }

    [Fact]
    public void Unknown_dependency_and_default_context_fail_with_clear_messages()
    {
        ItemBuildContext missing = Context(item: _ => throw new KeyNotFoundException());
        KeyNotFoundException unknown = Assert.Throws<KeyNotFoundException>(() => missing.ResolveItem("example:missing"));
        Assert.Contains("example:missing", unknown.Message);

        InvalidOperationException uninitialized = Assert.Throws<InvalidOperationException>(
            () => default(ItemBuildContext).ResolveBlock("example:block"));
        Assert.Contains(nameof(ItemBuildContext), uninitialized.Message);
    }

    [Fact]
    public void Declarative_item_type_selects_provider_and_validates_unknown_types()
    {
        ItemBuildContext context = Context();
        var providers = new ItemBehaviorProviderRegistry();
        JsonElement tool = Behavior("""{"Type":"tool","Material":"iron","Kind":"pickaxe"}""");

        Assert.IsType<ToolBehavior>(providers.Build(ResourceLocation.Parse("tool"), tool, context));
        ArgumentException error = Assert.Throws<ArgumentException>(() => providers.Build(
            ResourceLocation.Parse("example:missing"), Behavior("""{"Type":"example:missing"}"""), context));
        Assert.Contains("example:missing", error.Message);
    }

    [Fact]
    public void Item_factory_builds_a_bounded_ordered_behavior_collection()
    {
        ItemBuildContext context = Context();
        var providers = new ItemBehaviorProviderRegistry(new Dictionary<ResourceLocation, ItemBehaviorProviderRegistry.BehaviorFactory>
        {
            [ResourceLocation.Parse("example:first")] = static (_, _) => new FirstTestBehavior(),
            [ResourceLocation.Parse("example:second")] = static (_, _) => new SecondTestBehavior()
        });
        ItemDefinition definition = new()
        {
            ProtocolId = 31996,
            Behaviors = [Behavior("""{"Type":"example:first"}"""), Behavior("""{"Type":"example:second"}""")]
        };

        Item item = ItemFactory.Create(definition, context, providers);
        Assert.Equal(2, item.BehaviorCount);
        Assert.NotNull(item.GetBehavior<FirstTestBehavior>());
        Assert.NotNull(item.GetBehavior<SecondTestBehavior>());
        Assert.Throws<InvalidOperationException>(() => item.AddBehavior(new FirstTestBehavior()));
        item.AddBehavior(new TestBehavior3());
        item.AddBehavior(new TestBehavior4());
        item.AddBehavior(new TestBehavior5());
        item.AddBehavior(new TestBehavior6());
        item.AddBehavior(new TestBehavior7());
        item.AddBehavior(new TestBehavior8());
        Assert.Throws<InvalidOperationException>(() => item.AddBehavior(new OverflowTestBehavior()));
        item.Freeze();
        Assert.Throws<InvalidOperationException>(() => item.AddBehavior(new OverflowTestBehavior()));
    }

    private static ItemBuildContext Context(
        Func<ResourceLocation, Item>? item = null,
        Func<string, int>? texture = null)
    {
        Block block = BlockRegistry.Get("stone");
        Item fallbackItem = ContentRuntime.Current.Items.Get("omniblock:stick");
        return new ItemBuildContext(
            _ => block,
            _ => fallbackItem,
            item ?? (_ => fallbackItem),
            _ => ToolMaterialRegistry.Get("iron"),
            _ => ArmorMaterialRegistry.Get("iron"),
            _ => MaterialRegistry.Get("wood"),
            texture ?? (_ => 0),
            _ => TestEntityCatalog.ByName("snowball"),
            _ => BlockEntity.Generic,
            _ => new RecipeDefinition(),
            _ => new object());
    }

    private sealed class RecordingProvider(List<string> calls) : IItemBehaviorProviderRegistry
    {
        public IItemBehavior Build(ResourceLocation type, JsonElement definition, in ItemBuildContext context)
        {
            calls.Add($"type:{type.Path}");
            return new ShearsBehavior();
        }
    }

    private sealed class FirstTestBehavior : IItemBehavior;
    private sealed class SecondTestBehavior : IItemBehavior;
    private sealed class TestBehavior3 : IItemBehavior;
    private sealed class TestBehavior4 : IItemBehavior;
    private sealed class TestBehavior5 : IItemBehavior;
    private sealed class TestBehavior6 : IItemBehavior;
    private sealed class TestBehavior7 : IItemBehavior;
    private sealed class TestBehavior8 : IItemBehavior;
    private sealed class OverflowTestBehavior : IItemBehavior;

    private static JsonElement Behavior(string json) => JsonSerializer.Deserialize<JsonElement>(json);
}
