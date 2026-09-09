using OmniBlock.Registries;
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

    [Fact]
    public void Builders_create_independent_world_type_instances()
    {
        var firstBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        var secondBuilder = ContentRuntimeBuilder.CreateBuiltIns();
        firstBuilder.AddWorldTypeDefinition(Definition("example", "custom", "omniblock:overworld"));
        secondBuilder.AddWorldTypeDefinition(Definition("example", "custom", "omniblock:overworld"));

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
}
