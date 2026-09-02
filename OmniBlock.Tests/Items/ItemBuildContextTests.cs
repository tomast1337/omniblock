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
        Item item = Item.ByName("stick");
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
            Behavior = new ShearsBehaviorDefinition()
        };

        try
        {
            Item item = ItemFactory.Create(definition, context, providers);

            Assert.Equal(91, item.GetTextureId(0));
            Assert.IsType<ShearsBehavior>(item.GetBehavior<IItemBehavior>());
            Assert.Equal(["texture:example:icon", "behavior:ShearsBehaviorDefinition"], calls);
        }
        finally
        {
            Item.Items[definition.ProtocolId] = null;
        }
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

    private static ItemBuildContext Context(
        Func<ResourceLocation, Item>? item = null,
        Func<string, int>? texture = null)
    {
        Block block = BlockRegistry.Get("stone");
        Item fallbackItem = Item.ByName("stick");
        return new ItemBuildContext(
            _ => block,
            _ => fallbackItem,
            item ?? (_ => fallbackItem),
            _ => ToolMaterialRegistry.Get("iron"),
            _ => ArmorMaterialRegistry.Get("iron"),
            _ => MaterialRegistry.Get("wood"),
            texture ?? (_ => 0),
            _ => EntityRegistry.ByName("snowball"),
            _ => BlockEntity.Generic,
            _ => new RecipeDefinition(),
            _ => new object());
    }

    private sealed class RecordingProvider(List<string> calls) : IItemBehaviorProviderRegistry
    {
        public IItemBehavior Build(ItemBehaviorDefinition definition, ItemBuildContext context)
        {
            calls.Add($"behavior:{definition.GetType().Name}");
            return new ShearsBehavior();
        }
    }
}
