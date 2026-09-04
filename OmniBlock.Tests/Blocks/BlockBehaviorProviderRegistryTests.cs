using System.Text.Json;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Blocks.Materials;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockBehaviorProviderRegistryTests
{
    [Fact]
    public void Each_content_builder_owns_a_distinct_provider_registry()
    {
        var first = ContentRuntimeBuilder.CreateBuiltIns();
        var second = ContentRuntimeBuilder.CreateBuiltIns();

        Assert.NotSame(first.BlockBehaviorProviders, second.BlockBehaviorProviders);
    }

    [Fact]
    public void Build_context_resolves_every_dependency_through_injected_functions()
    {
        var expectedBlock = TestBlocks.Get("stone");
        var expectedItem = ContentRuntime.Current.Items.Get("omniblock:stick");
        var expectedMaterial = MaterialRegistry.Get("wood");
        List<string> calls = [];

        BehaviorBuildContext context = new(
            key =>
            {
                calls.Add($"block:{key}");
                return expectedBlock;
            },
            key =>
            {
                calls.Add($"item:{key}");
                return expectedItem;
            },
            key =>
            {
                calls.Add($"material:{key}");
                return expectedMaterial;
            },
            key =>
            {
                calls.Add($"texture:{key}");
                return 73;
            });

        Assert.Same(expectedBlock, context.ResolveBlock("example:block"));
        Assert.Same(expectedItem, context.ResolveItem("example:item"));
        Assert.Same(expectedMaterial, context.ResolveMaterial("example:material"));
        Assert.Equal(73, context.ResolveTerrainTexture("example/texture"));
        Assert.Equal(
            ["block:example:block", "item:example:item", "material:example:material", "texture:example/texture"],
            calls);
    }

    [Fact]
    public void Built_in_provider_builds_existing_behavior_by_namespaced_key()
    {
        var content = ContentRuntimeBuilder.CreateBuiltIns();
        var definition = JsonSerializer.Deserialize<JsonElement>(
            """{"Type":"button","Slots":["Physics"]}""");

        var behavior = content.BuildBlockBehavior("omniblock:button", definition);

        Assert.IsType<ButtonBehavior>(behavior);
    }

    [Fact]
    public void Built_in_provider_rejects_foreign_namespace_instead_of_using_its_path()
    {
        var content = ContentRuntimeBuilder.CreateBuiltIns();
        var definition = JsonSerializer.Deserialize<JsonElement>(
            """{"Type":"example:button","Slots":["Physics"]}""");

        var error = Assert.Throws<ArgumentException>(() => content.BuildBlockBehavior("example:button", definition));

        Assert.Contains("example:button", error.Message);
    }

    [Fact]
    public void Built_in_factories_resolve_all_content_dependencies_through_the_builder_context()
    {
        var block = TestBlocks.Get("dirt");
        var item = ContentRuntime.Current.Items.Get("omniblock:snowball");
        var material = MaterialRegistry.Get("wood");
        List<string> calls = [];
        BehaviorBuildContext context = new(
            key =>
            {
                calls.Add($"block:{key}");
                return block;
            },
            key =>
            {
                calls.Add($"item:{key}");
                return item;
            },
            key =>
            {
                calls.Add($"material:{key}");
                return material;
            },
            key =>
            {
                calls.Add($"texture:{key}");
                return 73;
            });
        var content = ContentRuntimeBuilder.CreateBuiltIns(context);

        _ = content.BuildBlockBehavior("omniblock:door", Json("""{"material":"example:wood"}"""));
        _ = content.BuildBlockBehavior("omniblock:grass_ticker", Json("""
                                                                      {
                                                                        "soil":"example:soil",
                                                                        "die_light_threshold":4,
                                                                        "die_chance_one_in":4,
                                                                        "spread_light_threshold":9
                                                                      }
                                                                      """));
        _ = content.BuildBlockBehavior("omniblock:snow", Json("""
                                                              {"drop_item":"example:snowball","drop_spread":0.7}
                                                              """));
        _ = content.BuildBlockBehavior("omniblock:sapling", Json("""
                                                                 {"textures":["example/sapling"]}
                                                                 """));

        Assert.Equal(
            [
                "material:example:wood",
                "block:example:soil",
                "item:example:snowball",
                "texture:example/sapling"
            ],
            calls);
    }

    [Fact]
    public void Default_context_fails_clearly_instead_of_null_reference()
    {
        BehaviorBuildContext context = default;

        var error = Assert.Throws<InvalidOperationException>(() => context.ResolveBlock("omniblock:stone"));

        Assert.Contains(nameof(BehaviorBuildContext), error.Message);
    }

    private static JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);
}
