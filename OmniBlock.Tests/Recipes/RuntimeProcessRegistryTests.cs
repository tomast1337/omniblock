using System.Text.Json;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Entities;
using OmniBlock.Inventories;
using OmniBlock.Items;
using OmniBlock.Items.Behaviors;
using OmniBlock.Processes;
using OmniBlock.Registries;
using OmniBlock.Screens;
using OmniBlock.Tests.TestSupport;

namespace OmniBlock.Tests.Recipes;

public sealed class RuntimeProcessRegistryTests
{
    [Fact]
    public void Published_runtime_contains_generic_and_typed_indexes_for_every_shipped_process()
    {
        RuntimeProcessRegistry processes = ContentRuntime.Current.Processes;

        Assert.Equal(160, processes.Count);
        Assert.Equal(150, processes.Crafting.Count);
        Assert.Equal(10, processes.Smelting.Count);
        Assert.Equal(119, processes.GetByType(ProcessTypes.CraftingShaped).Count);
        Assert.Equal(31, processes.GetByType(ProcessTypes.CraftingShapeless).Count);
        Assert.Equal(10, processes.GetByType(ProcessTypes.Smelting).Count);
        Assert.Equal(ProcessTypes.CraftingShaped,
            processes.Get("omniblock:stick").ProviderType);
    }

    [Fact]
    public void Typed_crafting_view_preserves_existing_crafting_behavior()
    {
        RuntimeItemRegistry items = ContentRuntime.Current.Items;
        var input = new InventoryCrafting(new TestScreenHandler(), 3, 3);
        input.SetStack(0, new ItemStack(items.Get("omniblock:planks")));
        input.SetStack(3, new ItemStack(items.Get("omniblock:planks")));

        ItemStack? result = ContentRuntime.Current.Processes.Crafting.Craft(input);

        Assert.NotNull(result);
        Assert.Same(items.Get("omniblock:stick"), result.GetItem());
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void Typed_smelting_view_returns_a_fresh_result_stack()
    {
        RuntimeItemRegistry items = ContentRuntime.Current.Items;
        ItemStack input = new(items.Get("omniblock:iron_ore"));

        ICompiledSmeltingProcess? process = ContentRuntime.Current.Processes.Smelting.Find(input);
        ItemStack? first = ContentRuntime.Current.Processes.Smelting.Smelt(input);
        ItemStack? second = ContentRuntime.Current.Processes.Smelting.Smelt(input);

        Assert.NotNull(process);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(items.Get("omniblock:iron_ore").Id, process.Input.Item.Id);
        Assert.Equal(items.Get("omniblock:ingot_iron").Id, process.Output.Item.Id);
        Assert.Same(items.Get("omniblock:ingot_iron"), first.GetItem());
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Compiled_processes_describe_matching_and_results_without_machine_execution_state()
    {
        RuntimeItemRegistry items = ContentRuntime.Current.Items;
        var input = new InventoryCrafting(new TestScreenHandler(), 3, 3);
        input.SetStack(0, new ItemStack(items.Get("omniblock:planks")));
        input.SetStack(3, new ItemStack(items.Get("omniblock:planks")));

        ICompiledCraftingProcess? process = ContentRuntime.Current.Processes.Crafting.Find(input);

        Assert.NotNull(process);
        Assert.True(process.Matches(input));
        Assert.Equal(2, process.IngredientCount);
        Assert.Same(items.Get("omniblock:stick"), process.Output.Item);
        Assert.Equal(4, process.Output.Count);
        Assert.DoesNotContain(typeof(ICompiledProcess).GetMethods(), method =>
            method.Name is "Tick" or "Execute" or "Advance");
        Assert.DoesNotContain(typeof(ICompiledProcess).Assembly.GetTypes()
            .Where(type => type.GetInterfaces().Contains(typeof(ICompiledProcess)))
            .SelectMany(type => type.GetProperties()), property =>
                property.Name is "Progress" or "Inventory" or "NetworkState");
    }

    [Fact]
    public void Player_crafting_screen_uses_the_world_process_runtime()
    {
        var player = new TestEntityPlayer(new FakeWorldContext());
        var screen = Assert.IsType<PlayerScreenHandler>(player.PlayerScreenHandler);
        Item planks = player.World.Content.Items.Get("omniblock:planks");

        screen.craftingInput.SetStack(0, new ItemStack(planks));
        screen.craftingInput.SetStack(2, new ItemStack(planks));

        ItemStack? result = screen.craftingResult.GetStack(0);
        Assert.NotNull(result);
        Assert.Same(player.World.Content.Items.Get("omniblock:stick"), result.GetItem());
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void Crafted_statistics_are_discovered_from_published_process_outputs()
    {
        RuntimeItemRegistry items = ContentRuntime.Current.Items;

        Assert.NotNull(OmniBlock.Stats.Stats.Crafted[items.Get("omniblock:stick").Id]);
        Assert.NotNull(OmniBlock.Stats.Stats.Crafted[items.Get("omniblock:ingot_iron").Id]);
    }

    [Fact]
    public void Unknown_custom_type_returns_an_empty_read_only_view()
    {
        IReadOnlyList<ICompiledProcess> entries = ContentRuntime.Current.Processes.GetByType("example:crusher");

        Assert.Empty(entries);
        Assert.False(ContentRuntime.Current.Processes.TryGet("example:missing", out _));
    }

    [Fact]
    public void Builder_rejects_duplicate_process_ids_before_publishing_a_candidate()
    {
        ContentRuntime published = ContentRuntime.Current;
        var provider = new TestProvider();
        ContentRuntimeBuilder builder = Builder(provider);
        builder.AddProcessDefinition(Definition("example:same", "example:test"));
        builder.AddProcessDefinition(Definition("example:same", "example:test"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains("Duplicate process id 'example:same'", error.Message);
        Assert.Same(published, ContentRuntime.Current);
        Assert.Equal(1, provider.BuildCalls);
    }

    [Fact]
    public void Provider_specific_conflict_validation_fails_the_entire_candidate()
    {
        ContentRuntime published = ContentRuntime.Current;
        var provider = new TestProvider(rejectMultiple: true);
        ContentRuntimeBuilder builder = Builder(provider);
        builder.AddProcessDefinition(Definition("example:first", "example:test"));
        builder.AddProcessDefinition(Definition("example:second", "example:test"));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains("example:test", error.Message);
        Assert.Contains("provider overlap", error.Message);
        Assert.Same(published, ContentRuntime.Current);
    }

    private static ContentRuntimeBuilder Builder(IProcessProvider provider)
    {
        var providers = new ProcessProviderRegistry(
        [
            new KeyValuePair<ResourceLocation, IProcessProvider>("example:test", provider)
        ]);
        return new ContentRuntimeBuilder(
            new NoBlockBehaviorProviders(), default,
            new NoItemBehaviorProviders(), default,
            processProviders: providers);
    }

    private static ProcessDefinition Definition(string id, string type)
    {
        ProcessDefinition definition = JsonSerializer.Deserialize<ProcessDefinition>(
            $$"""{"id":"{{id}}","type":"{{type}}","value":1}""")!;
        return definition;
    }

    private sealed record TestProcess(ResourceLocation Id, ResourceLocation ProviderType) : ICompiledProcess;

    private sealed class TestProvider(bool rejectMultiple = false) : IProcessProvider
    {
        public int BuildCalls { get; private set; }

        public ICompiledProcess Build(
            ResourceLocation id,
            JsonElement definition,
            in ProcessBuildContext context)
        {
            BuildCalls++;
            Assert.Equal(1, definition.GetProperty("value").GetInt32());
            return new TestProcess(id, "example:test");
        }

        public void Validate(IReadOnlyList<ICompiledProcess> processes)
        {
            if (rejectMultiple && processes.Count > 1)
                throw new InvalidOperationException("provider overlap");
        }
    }

    private sealed class NoBlockBehaviorProviders : IBlockBehaviorProviderRegistry
    {
        public object Build(ResourceLocation type, JsonElement definition, in BehaviorBuildContext context) =>
            throw new NotSupportedException();
    }

    private sealed class NoItemBehaviorProviders : IItemBehaviorProviderRegistry
    {
        public IItemBehavior Build(ResourceLocation type, JsonElement definition, in ItemBuildContext context) =>
            throw new NotSupportedException();
    }

    private sealed class TestScreenHandler : ScreenHandler
    {
        public override bool canUse(EntityPlayer entityPlayer) => true;
    }
}
