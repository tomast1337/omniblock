using System.Text.Json;
using System.Text.Json.Nodes;
using OmniBlock.Worlds;
using OmniBlock.Worlds.Generation;

namespace OmniBlock.Tests.Worlds;

public sealed class WorldTypeBuilderTests
{
    [Fact]
    public void Builder_compiles_provider_backed_world_type_definitions()
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddWorldTypeDefinition(Definition("example", "islands", "omniblock:sky"));

        var runtime = builder.Build();
        var type = runtime.WorldTypes.Get("example:islands");

        Assert.Equal("islands", type.Name);
        Assert.Equal(BuiltInWorldGeneratorProviders.Sky, type.GeneratorProviderType);
        Assert.Same(builder.WorldGeneratorProviders, runtime.WorldGeneratorProviders);
    }

    [Fact]
    public void Unknown_generator_provider_identifies_the_owning_world_type()
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddWorldTypeDefinition(Definition("example", "broken", "missing:generator"));
        var published = ContentRuntime.Current;

        var error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("example:broken", error.Message);
        Assert.Contains("missing:generator", error.Message);
        Assert.Same(published, ContentRuntime.Current);
    }

    [Theory]
    [InlineData("omniblock:overworld", "stone")]
    [InlineData("omniblock:sky", "stone")]
    [InlineData("omniblock:flat", "water")]
    public void Unknown_generator_block_reference_fails_before_runtime_publication(
        string provider,
        string role)
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        var definition = Definition("example", "broken_blocks", provider);
        definition = new WorldTypeDefinition
        {
            Namespace = definition.Namespace,
            Name = definition.Name,
            Generator = definition.Generator,
            IconPath = definition.IconPath,
            CanBeCreated = definition.CanBeCreated,
            GeneratorSettings = GeneratorSettings(provider, (role, "example:missing_block"))
        };
        builder.AddWorldTypeDefinition(definition);
        var published = ContentRuntime.Current;

        var error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("example:broken_blocks", error.Message);
        Assert.Contains(role, error.Message);
        Assert.Contains("example:missing_block", error.Message);
        Assert.Same(published, ContentRuntime.Current);
    }

    [Fact]
    public void Duplicate_world_type_resource_ids_fail_atomically()
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddWorldTypeDefinition(Definition("example", "duplicate", "omniblock:flat"));
        builder.AddWorldTypeDefinition(Definition("example", "duplicate", "omniblock:sky"));
        var published = ContentRuntime.Current;

        var error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("Duplicate world type 'example:duplicate'", error.Message);
        Assert.Same(published, ContentRuntime.Current);
    }

    [Theory]
    [InlineData("omniblock:overworld", "MinLimitOctaves", 0)]
    [InlineData("omniblock:overworld", "DungeonAttempts", -1)]
    [InlineData("omniblock:sky", "HorizontalNoiseScale", 0)]
    [InlineData("omniblock:sky", "IronAttempts", -1)]
    [InlineData("omniblock:flat", "DungeonAttempts", -1)]
    public void Invalid_numeric_generator_settings_fail_during_compilation(
        string provider,
        string setting,
        int value)
    {
        var providers = BuiltInWorldGeneratorProviders.CreateRegistry();

        var error = Assert.Throws<InvalidOperationException>(() => providers.Compile(
            ResourceLocation.Parse(provider),
            "example:invalid_settings",
            GeneratorSettingsWithValue(provider, setting, value),
            new WorldGeneratorCompileContext(ContentRuntime.Current.Blocks)));

        Assert.Contains("example:invalid_settings", error.Message);
        Assert.Contains(setting, error.Message);
    }

    [Theory]
    [InlineData("omniblock:overworld", "default", "WaterLakeChance")]
    [InlineData("omniblock:sky", "sky", "GoldMaxY")]
    [InlineData("omniblock:flat", "flat", "LavaSpringUpperY")]
    public void Invalid_feature_settings_fail_during_compilation(
        string provider,
        string assetName,
        string setting)
    {
        var definition = LoadSettings(assetName);
        definition["Features"]![setting] = 0;
        var providers = BuiltInWorldGeneratorProviders.CreateRegistry();

        var error = Assert.Throws<InvalidOperationException>(() => providers.Compile(
            ResourceLocation.Parse(provider),
            "example:invalid_features",
            JsonSerializer.SerializeToElement(definition),
            new WorldGeneratorCompileContext(ContentRuntime.Current.Blocks)));

        Assert.Contains("example:invalid_features", error.Message);
        Assert.Contains(setting, error.Message);
    }

    [Fact]
    public void Builders_create_independent_world_type_instances()
    {
        var firstBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        var secondBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        firstBuilder.AddWorldTypeDefinition(Definition("example", "custom", "omniblock:sky"));
        secondBuilder.AddWorldTypeDefinition(Definition("example", "custom", "omniblock:sky"));

        var first = firstBuilder.Build();
        var second = secondBuilder.Build();

        Assert.NotSame(first.WorldTypes, second.WorldTypes);
        Assert.NotSame(first.WorldTypes.Get("example:custom"), second.WorldTypes.Get("example:custom"));
    }

    private static WorldTypeDefinition Definition(
        string @namespace,
        string name,
        string generator) => new()
    {
        Namespace = Namespace.Get(@namespace),
        Name = name,
        Generator = generator,
        IconPath = "/example.png",
        CanBeCreated = true
    };

    private static JsonElement GeneratorSettings(
        string provider,
        params (string Role, string Reference)[] replacements)
    {
        var roles = GeneratorRoles(provider);
        var blocks = roles.ToDictionary(static role => role, static role => $"omniblock:{role}");
        foreach (var (role, reference) in replacements) blocks[role] = reference;
        return JsonSerializer.SerializeToElement(new
        {
            Blocks = blocks
        });
    }

    private static JsonElement GeneratorSettingsWithValue(
        string provider,
        string setting,
        int value)
    {
        var blocks = GeneratorRoles(provider)
            .ToDictionary(static role => role, static role => $"omniblock:{role}");
        var settings = new Dictionary<string, object>
        {
            ["Blocks"] = blocks,
            [setting] = value
        };
        return JsonSerializer.SerializeToElement(settings);
    }

    private static string[] GeneratorRoles(string provider) => provider switch
    {
        "omniblock:overworld" =>
        [
            "stone", "water", "flowing_water", "lava", "flowing_lava", "ice", "bedrock",
            "dirt", "gravel", "sand", "sandstone", "coal_ore", "iron_ore", "gold_ore",
            "redstone_ore", "diamond_ore", "lapis_ore", "dandelion", "grass", "dead_bush",
            "rose", "brown_mushroom", "red_mushroom", "snow"
        ],
        "omniblock:sky" =>
        [
            "stone", "water", "flowing_water", "lava", "flowing_lava", "dirt", "gravel",
            "sand", "sandstone", "coal_ore", "iron_ore", "gold_ore", "redstone_ore",
            "diamond_ore", "lapis_ore", "dandelion", "rose", "brown_mushroom",
            "red_mushroom", "snow"
        ],
        "omniblock:flat" =>
        [
            "water", "flowing_water", "lava", "flowing_lava", "dirt", "gravel", "coal_ore",
            "iron_ore", "gold_ore", "redstone_ore", "diamond_ore", "lapis_ore", "dandelion",
            "rose", "brown_mushroom", "red_mushroom", "dead_bush", "grass"
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };

    private static JsonObject LoadSettings(string assetName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "world_type", $"{assetName}.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        return root["GeneratorSettings"]!.AsObject();
    }
}
