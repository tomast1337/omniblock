using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Items;
using OmniBlock.Processes;

namespace OmniBlock.Tests.Recipes;

public sealed class ProcessProviderRegistryTests
{
    [Fact]
    public void Built_in_process_type_keys_are_namespaced_and_stable()
    {
        Assert.Equal("omniblock:crafting_shaped", ProcessTypes.CraftingShaped.ToString());
        Assert.Equal("omniblock:crafting_shapeless", ProcessTypes.CraftingShapeless.ToString());
        Assert.Equal("omniblock:smelting", ProcessTypes.Smelting.ToString());
    }

    [Fact]
    public void Registry_dispatches_exact_namespaced_type_and_passes_id_json_and_context()
    {
        var expectedItem = ContentRuntime.Current.Items.Get("omniblock:stick");
        var expectedBlock = ContentRuntime.Current.Blocks.Get("omniblock:stone");
        var provider = new RecordingProvider();
        var registry = Registry(("example:crusher", provider));
        var context = new ProcessBuildContext(ContentRuntime.Current.Items, ContentRuntime.Current.Blocks);
        var definition = Json("""{"energy":4000}""");

        var compiled = registry.Build(
            "example:crusher", "example:crushed_iron", definition, context);

        Assert.Equal("example:crushed_iron", compiled.Id.ToString());
        Assert.Equal("example:crusher", compiled.ProviderType.ToString());
        Assert.Equal(4000, provider.Energy);
        Assert.Same(expectedItem, provider.Item);
        Assert.Same(expectedBlock, provider.Block);
    }

    [Fact]
    public void Foreign_namespace_does_not_fall_back_to_provider_path()
    {
        var provider = new RecordingProvider();
        var registry = Registry(("omniblock:smelting", provider));
        var context = new ProcessBuildContext(ContentRuntime.Current.Items, ContentRuntime.Current.Blocks);

        var error = Assert.Throws<ArgumentException>(() => registry.Build(
            "example:smelting", "example:test", Json("{}"), context));

        Assert.Contains("example:test", error.Message);
        Assert.Contains("example:smelting", error.Message);
        Assert.Equal(0, provider.BuildCalls);
    }

    [Fact]
    public void Duplicate_provider_types_are_rejected()
    {
        var error = Assert.Throws<ArgumentException>(() => Registry(
            ("example:crusher", new RecordingProvider()),
            ("example:crusher", new RecordingProvider())));

        Assert.Contains("example:crusher", error.Message);
    }

    [Fact]
    public void Provider_failures_identify_owning_process_and_provider_type()
    {
        var registry = Registry(("example:crusher", new ThrowingProvider()));
        var context = new ProcessBuildContext(ContentRuntime.Current.Items, ContentRuntime.Current.Blocks);

        var error = Assert.Throws<InvalidOperationException>(() => registry.Build(
            "example:crusher", "example:bad_recipe", Json("{}"), context));

        Assert.Contains("example:bad_recipe", error.Message);
        Assert.Contains("example:crusher", error.Message);
        Assert.Contains("missing catalyst", error.Message);
    }

    [Fact]
    public void Registry_rejects_provider_returning_wrong_identity()
    {
        var registry = Registry(("example:crusher", new WrongIdentityProvider()));
        var context = new ProcessBuildContext(ContentRuntime.Current.Items, ContentRuntime.Current.Blocks);

        var error = Assert.Throws<InvalidOperationException>(() => registry.Build(
            "example:crusher", "example:expected", Json("{}"), context));

        Assert.Contains("example:other", error.Message);
        Assert.Contains("example:expected", error.Message);
    }

    [Fact]
    public void Default_build_context_fails_clearly_for_missing_dependencies()
    {
        ProcessBuildContext context = default;

        var itemError = Assert.Throws<InvalidOperationException>(() => context.ResolveItem("omniblock:stick"));
        var blockError = Assert.Throws<InvalidOperationException>(() => context.ResolveBlock("omniblock:stone"));

        Assert.Contains(nameof(ProcessBuildContext.Items), itemError.Message);
        Assert.Contains(nameof(ProcessBuildContext.Blocks), blockError.Message);
    }

    private static ProcessProviderRegistry Registry(
        params (ResourceLocation Type, IProcessProvider Provider)[] providers) => new(
        providers.Select(entry =>
            new KeyValuePair<ResourceLocation, IProcessProvider>(entry.Type, entry.Provider)));

    private static JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);

    private sealed record CompiledProcess(
        ResourceLocation Id,
        ResourceLocation ProviderType) : ICompiledProcess;

    private sealed class RecordingProvider : IProcessProvider
    {
        public int BuildCalls { get; private set; }
        public int Energy { get; private set; }
        public Item? Item { get; private set; }
        public Block? Block { get; private set; }

        public ICompiledProcess Build(
            ResourceLocation id,
            JsonElement definition,
            in ProcessBuildContext context)
        {
            BuildCalls++;
            Energy = definition.GetProperty("energy").GetInt32();
            Item = context.ResolveItem("omniblock:stick");
            Block = context.ResolveBlock("omniblock:stone");
            return new CompiledProcess(id, "example:crusher");
        }
    }

    private sealed class ThrowingProvider : IProcessProvider
    {
        public ICompiledProcess Build(
            ResourceLocation id,
            JsonElement definition,
            in ProcessBuildContext context) => throw new KeyNotFoundException("missing catalyst");
    }

    private sealed class WrongIdentityProvider : IProcessProvider
    {
        public ICompiledProcess Build(
            ResourceLocation id,
            JsonElement definition,
            in ProcessBuildContext context) => new CompiledProcess("example:other", "example:crusher");
    }
}
