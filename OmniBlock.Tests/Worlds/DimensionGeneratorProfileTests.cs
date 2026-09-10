using System.Text.Json;
using System.Text.Json.Nodes;
using OmniBlock.Worlds.Generation;

namespace OmniBlock.Tests.Worlds;

public sealed class DimensionGeneratorProfileTests
{
    [Fact]
    public void Shipped_nether_profile_is_compiled_into_the_content_runtime()
    {
        var profile = ContentRuntime.Current.DimensionGeneratorProfiles.Get("omniblock:nether");

        Assert.Equal(-1, profile.DimensionId);
        Assert.Equal(BuiltInWorldGeneratorProviders.Nether, profile.GeneratorProviderType);
        Assert.Same(
            profile,
            ContentRuntime.Current.DimensionGeneratorProfiles.GetByDimensionId(-1));
    }

    [Fact]
    public void Unknown_nether_block_reference_fails_before_runtime_publication()
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddDimensionGeneratorProfile(Definition(
            "example",
            "broken_nether",
            -37,
            NetherSettings(("netherrack", "example:missing_netherrack"))));
        var published = ContentRuntime.Current;

        var error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("example:broken_nether", error.Message);
        Assert.Contains("netherrack", error.Message);
        Assert.Contains("example:missing_netherrack", error.Message);
        Assert.Same(published, ContentRuntime.Current);
    }

    [Fact]
    public void Duplicate_dimension_ids_fail_atomically()
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddDimensionGeneratorProfile(Definition("example", "first", -37));
        builder.AddDimensionGeneratorProfile(Definition("example", "second", -37));
        var published = ContentRuntime.Current;

        var error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("Duplicate dimension generator profile id -37", error.Message);
        Assert.Same(published, ContentRuntime.Current);
    }

    [Theory]
    [InlineData("LavaLevel", -1)]
    [InlineData("SurfaceLevel", 128)]
    [InlineData("MinLimitOctaves", 0)]
    [InlineData("LavaSpringAttempts", -1)]
    public void Invalid_nether_numeric_settings_fail_during_compilation(
        string setting,
        int value)
    {
        var definition = InvalidNumericSettings(setting, value);
        var providers = BuiltInWorldGeneratorProviders.CreateRegistry();

        var error = Assert.Throws<InvalidOperationException>(() => providers.Compile(
            BuiltInWorldGeneratorProviders.Nether,
            "example:invalid_nether",
            definition,
            new WorldGeneratorCompileContext(ContentRuntime.Current.Blocks)));

        Assert.Contains("example:invalid_nether", error.Message);
        Assert.Contains(setting, error.Message);
    }

    [Fact]
    public void Invalid_nether_feature_range_fails_during_compilation()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "assets",
            "dimension_generator",
            "nether.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var settings = root["GeneratorSettings"]!.AsObject();
        settings["FireClusterBound"] = 0;
        var providers = BuiltInWorldGeneratorProviders.CreateRegistry();

        var error = Assert.Throws<InvalidOperationException>(() => providers.Compile(
            BuiltInWorldGeneratorProviders.Nether,
            "example:invalid_nether_features",
            JsonSerializer.SerializeToElement(settings),
            new WorldGeneratorCompileContext(ContentRuntime.Current.Blocks)));

        Assert.Contains("example:invalid_nether_features", error.Message);
        Assert.Contains("FireClusterBound", error.Message);
    }

    [Fact]
    public void Builders_create_independent_dimension_generator_profiles()
    {
        var firstBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        var secondBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        firstBuilder.AddDimensionGeneratorProfile(Definition("example", "custom", -37));
        secondBuilder.AddDimensionGeneratorProfile(Definition("example", "custom", -37));

        var first = firstBuilder.Build();
        var second = secondBuilder.Build();

        Assert.NotSame(first.DimensionGeneratorProfiles, second.DimensionGeneratorProfiles);
        Assert.NotSame(
            first.DimensionGeneratorProfiles.Get("example:custom"),
            second.DimensionGeneratorProfiles.Get("example:custom"));
    }

    private static DimensionGeneratorProfileDefinition Definition(
        string @namespace,
        string name,
        int dimensionId,
        JsonElement settings = default) => new()
    {
        Namespace = Namespace.Get(@namespace),
        Name = name,
        DimensionId = dimensionId,
        Generator = BuiltInWorldGeneratorProviders.Nether.ToString(),
        GeneratorSettings = settings
    };

    private static JsonElement NetherSettings(
        params (string Role, string Reference)[] replacements)
    {
        string[] roles =
        [
            "netherrack", "lava", "flowing_lava", "bedrock", "gravel", "soulsand",
            "brown_mushroom", "red_mushroom"
        ];
        var blocks = roles.ToDictionary(static role => role, static role => $"omniblock:{role}");
        foreach (var (role, reference) in replacements) blocks[role] = reference;
        return JsonSerializer.SerializeToElement(new
        {
            Blocks = blocks
        });
    }

    private static JsonElement InvalidNumericSettings(string setting, int value)
    {
        var blocks = new Dictionary<string, string>
        {
            ["netherrack"] = "omniblock:netherrack",
            ["lava"] = "omniblock:lava",
            ["flowing_lava"] = "omniblock:flowing_lava",
            ["bedrock"] = "omniblock:bedrock",
            ["gravel"] = "omniblock:gravel",
            ["soulsand"] = "omniblock:soulsand",
            ["brown_mushroom"] = "omniblock:brown_mushroom",
            ["red_mushroom"] = "omniblock:red_mushroom"
        };
        var settings = new Dictionary<string, object>
        {
            ["Blocks"] = blocks,
            [setting] = value
        };
        return JsonSerializer.SerializeToElement(settings);
    }
}
