using System.Text.Json;
using OmniBlock.Registries;
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
        return JsonSerializer.SerializeToElement(new { Blocks = blocks });
    }
}
