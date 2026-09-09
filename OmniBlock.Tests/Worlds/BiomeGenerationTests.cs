using OmniBlock.Registries;
using OmniBlock.Worlds.Generation.Biomes;

namespace OmniBlock.Tests.Worlds;

public sealed class BiomeGenerationTests
{
    [Fact]
    public void Rainforest_fern_selection_is_owned_by_the_runtime_catalog()
    {
        var settings = ContentRuntime.Current.BiomeGeneration.Get("omniblock:rainforest");

        Assert.Equal(3, settings.FernSelectionBound);
        Assert.Equal(ResourceLocation.Parse("omniblock:rainforest"), Biome.Rainforest.Key);
    }

    [Fact]
    public void Invalid_fern_selection_bound_fails_atomically()
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddBiomeGenerationDefinition(Definition("example", "broken", -1));
        var published = ContentRuntime.Current;

        var error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("example:broken", error.Message);
        Assert.Contains("FernSelectionBound", error.Message);
        Assert.Same(published, ContentRuntime.Current);
    }

    [Fact]
    public void Duplicate_biome_generation_definitions_fail_atomically()
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddBiomeGenerationDefinition(Definition("example", "duplicate", 3));
        builder.AddBiomeGenerationDefinition(Definition("example", "duplicate", 4));
        var published = ContentRuntime.Current;

        var error = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("Duplicate biome generation definition 'example:duplicate'", error.Message);
        Assert.Same(published, ContentRuntime.Current);
    }

    [Fact]
    public void Builders_create_independent_biome_generation_registries()
    {
        var firstBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        var secondBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        firstBuilder.AddBiomeGenerationDefinition(Definition("example", "forest", 3));
        secondBuilder.AddBiomeGenerationDefinition(Definition("example", "forest", 5));

        var first = firstBuilder.Build();
        var second = secondBuilder.Build();

        Assert.NotSame(first.BiomeGeneration, second.BiomeGeneration);
        Assert.Equal(3, first.BiomeGeneration.Get("example:forest").FernSelectionBound);
        Assert.Equal(5, second.BiomeGeneration.Get("example:forest").FernSelectionBound);
    }

    private static BiomeGenerationDefinition Definition(
        string @namespace,
        string name,
        int fernSelectionBound) => new()
    {
        Namespace = Namespace.Get(@namespace),
        Name = name,
        FernSelectionBound = fernSelectionBound
    };
}
