using System.Text.Json;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Entities;
using OmniBlock.Inventories;
using OmniBlock.Items;
using OmniBlock.Items.Behaviors;
using OmniBlock.Processes;
using OmniBlock.Screens;

namespace OmniBlock.Tests.Recipes;

public sealed class RuntimeProcessRegistryTests
{
    [Fact]
    public void Published_runtime_contains_generic_and_typed_indexes_for_every_shipped_process()
    {
        var processes = ContentRuntime.Current.Processes;

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
        var items = ContentRuntime.Current.Items;
        var input = new InventoryCrafting(new TestScreenHandler(), 3, 3);
        input.SetStack(0, new ItemStack(items.Get("omniblock:planks")));
        input.SetStack(3, new ItemStack(items.Get("omniblock:planks")));

        var result = ContentRuntime.Current.Processes.Crafting.Craft(input);

        Assert.NotNull(result);
        Assert.Same(items.Get("omniblock:stick"), result.GetItem());
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void Typed_smelting_view_returns_a_fresh_result_stack()
    {
        var items = ContentRuntime.Current.Items;
        ItemStack input = new(items.Get("omniblock:iron_ore"));

        var process = ContentRuntime.Current.Processes.Smelting.Find(input);
        var first = ContentRuntime.Current.Processes.Smelting.Smelt(input);
        var second = ContentRuntime.Current.Processes.Smelting.Smelt(input);

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
        var items = ContentRuntime.Current.Items;
        var input = new InventoryCrafting(new TestScreenHandler(), 3, 3);
        input.SetStack(0, new ItemStack(items.Get("omniblock:planks")));
        input.SetStack(3, new ItemStack(items.Get("omniblock:planks")));

        var process = ContentRuntime.Current.Processes.Crafting.Find(input);

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
        var planks = player.World.Content.Items.Get("omniblock:planks");

        screen.craftingInput.SetStack(0, new ItemStack(planks));
        screen.craftingInput.SetStack(2, new ItemStack(planks));

        var result = screen.craftingResult.GetStack(0);
        Assert.NotNull(result);
        Assert.Same(player.World.Content.Items.Get("omniblock:stick"), result.GetItem());
        Assert.Equal(4, result.Count);
    }

    [Fact]
    public void Crafted_statistics_are_discovered_from_published_process_outputs()
    {
        var items = ContentRuntime.Current.Items;

        Assert.NotNull(Stats.Stats.Crafted[items.Get("omniblock:stick").Id]);
        Assert.NotNull(Stats.Stats.Crafted[items.Get("omniblock:ingot_iron").Id]);
    }

    [Fact]
    public void Unknown_custom_type_returns_an_empty_read_only_view()
    {
        var entries = ContentRuntime.Current.Processes.GetByType("example:crusher");

        Assert.Empty(entries);
        Assert.False(ContentRuntime.Current.Processes.TryGet("example:missing", out _));
    }

    [Fact]
    public void Builder_rejects_duplicate_process_ids_before_publishing_a_candidate()
    {
        var published = ContentRuntime.Current;
        var provider = new TestProvider();
        var builder = Builder(provider);
        builder.AddProcessDefinition(Definition("example:same", "example:test"));
        builder.AddProcessDefinition(Definition("example:same", "example:test"));

        var error = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains("Duplicate process id 'example:same'", error.Message);
        Assert.Same(published, ContentRuntime.Current);
        Assert.Equal(1, provider.BuildCalls);
    }

    [Fact]
    public void Provider_specific_conflict_validation_fails_the_entire_candidate()
    {
        var published = ContentRuntime.Current;
        var provider = new TestProvider(true);
        var builder = Builder(provider);
        builder.AddProcessDefinition(Definition("example:first", "example:test"));
        builder.AddProcessDefinition(Definition("example:second", "example:test"));

        var error = Assert.Throws<InvalidOperationException>(builder.Build);

        Assert.Contains("example:test", error.Message);
        Assert.Contains("provider overlap", error.Message);
        Assert.Same(published, ContentRuntime.Current);
    }

    [Fact]
    public void WithProcesses_creates_a_new_snapshot_while_sharing_immutable_blocks_and_items()
    {
        var current = ContentRuntime.Current;

        var replacement = current.WithProcesses(
        [
            BuiltInDefinition("example:coal_to_stick", """
                                                       {"type":"shaped","pattern":["#"],"key":{"#":"omniblock:coal"},
                                                        "result":{"id":"omniblock:stick","count":2}}
                                                       """)
        ]);

        Assert.NotSame(current, replacement);
        Assert.Same(current.Blocks, replacement.Blocks);
        Assert.Same(current.Items, replacement.Items);
        Assert.NotSame(current.Processes, replacement.Processes);
        Assert.Equal(160, current.Processes.Count);
        Assert.Equal(1, replacement.Processes.Count);
    }

    [Fact]
    public void Independently_built_runtimes_do_not_share_process_registries_or_instances()
    {
        var firstBuilder = Builder(new TestProvider());
        var secondBuilder = Builder(new TestProvider());
        firstBuilder.AddProcessDefinition(Definition("example:process", "example:test"));
        secondBuilder.AddProcessDefinition(Definition("example:process", "example:test"));

        var first = firstBuilder.Build();
        var second = secondBuilder.Build();

        Assert.NotSame(first, second);
        Assert.NotSame(first.Processes, second.Processes);
        Assert.NotSame(first.Processes.Get("example:process"), second.Processes.Get("example:process"));
        Assert.Equal(first.Processes.Get("example:process"), second.Processes.Get("example:process"));
    }

    [Fact]
    public void Failed_process_replacement_leaves_the_previous_snapshot_usable()
    {
        var current = ContentRuntime.Current;

        Assert.Throws<InvalidOperationException>(() => current.WithProcesses(
        [
            BuiltInDefinition("example:invalid", """
                                                 {"type":"shaped","pattern":["#"],"key":{"#":"example:missing"},
                                                  "result":{"id":"omniblock:stick"}}
                                                 """)
        ]));

        Assert.Same(current, ContentRuntime.Current);
        Assert.Equal(160, current.Processes.Count);
        Assert.NotNull(current.Processes.Get("omniblock:stick"));
    }

    [Fact]
    public void Existing_player_screen_observes_world_snapshot_replacement()
    {
        var world = new FakeWorldContext();
        var player = new TestEntityPlayer(world);
        var screen = Assert.IsType<PlayerScreenHandler>(player.PlayerScreenHandler);
        var replacement = world.Content.WithProcesses(
        [
            BuiltInDefinition("example:coal_to_stick", """
                                                       {"type":"shaped","pattern":["#"],"key":{"#":"omniblock:coal"},
                                                        "result":{"id":"omniblock:stick","count":2}}
                                                       """)
        ]);

        world.ReplaceContent(replacement);
        screen.craftingInput.SetStack(0, new ItemStack(world.Content.Items.Get("omniblock:coal")));

        var result = screen.craftingResult.GetStack(0);
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Same(world.Content.Items.Get("omniblock:stick"), result.GetItem());
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
        var definition = JsonSerializer.Deserialize<ProcessDefinition>(
            $$"""{"id":"{{id}}","type":"{{type}}","value":1}""")!;
        return definition;
    }

    private static ProcessDefinition BuiltInDefinition(string id, string json)
    {
        var definition = JsonSerializer.Deserialize<ProcessDefinition>(json)!;
        var key = ResourceLocation.Parse(id);
        definition.Namespace = key.Namespace;
        definition.Name = key.Path;
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
