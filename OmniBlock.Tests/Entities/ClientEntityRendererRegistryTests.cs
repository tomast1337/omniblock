using System.Text.Json;
using OmniBlock.Client.Rendering.Entities;
using OmniBlock.Entities;

namespace OmniBlock.Tests.Entities;

[Collection("EntityTests")]
public sealed class ClientEntityRendererRegistryTests
{
    [Fact]
    public void Shared_runtime_preserves_a_namespaced_immutable_render_descriptor()
    {
        var cow = ContentRuntime.Current.EntityTypes.Get("omniblock:cow");

        var descriptor = Assert.IsType<EntityRenderDescriptor>(cow.RenderDescriptor);
        Assert.Equal(ResourceLocation.Parse("omniblock:living"), descriptor.ProviderType);
        Assert.Equal("cow", descriptor.Definition.GetProperty("Model").GetString());
        var impostor = Assert.IsType<ClientEntityImpostorDescriptor>(
            ClientEntityImpostorDescriptor.Compile(descriptor.Definition));
        Assert.Equal(ResourceLocation.Parse("omniblock:basic"), impostor.ProviderType);
        Assert.Equal("/mob/cow.png", Assert.Single(impostor.Layers).Texture);
    }

    [Fact]
    public void Built_in_capture_dependencies_come_only_from_entity_descriptors()
    {
        var dependencies = new ClientEntityRendererRegistry().CaptureDependencies(ContentRuntime.Current);

        Assert.Equal(3, dependencies.Count);
        Assert.True(dependencies.Contains("/mob/cow.png"));
        Assert.True(dependencies.Contains("/mob/sheep.png"));
        Assert.True(dependencies.Contains("/mob/sheep_fur.png"));
    }

    [Fact]
    public void Unknown_client_provider_identifies_the_entity_and_provider()
    {
        var published = ContentRuntime.Current;
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddEntityDefinition(new EntityDefinition
        {
            Name = "client_only_failure",
            Namespace = Namespace.OmniBlock,
            ProtocolId = 20,
            Renderer = JsonSerializer.Deserialize<JsonElement>("""{"Type":"example:missing_renderer"}""")
        });
        var runtime = builder.Build();

        var error = Assert.Throws<InvalidOperationException>(() => new ClientEntityRendererRegistry().Build(runtime));

        Assert.Contains("omniblock:client_only_failure", error.Message);
        Assert.Contains("example:missing_renderer", error.Message);
        Assert.Same(published, ContentRuntime.Current);
    }

    [Fact]
    public void Unknown_impostor_provider_rejects_the_candidate_client_catalog()
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddEntityDefinition(new EntityDefinition
        {
            Name = "bad_impostor", Namespace = Namespace.OmniBlock, ProtocolId = 20,
            Renderer = JsonSerializer.Deserialize<JsonElement>("""
                {"Type":"living","Model":"cow","Impostor":{"Id":"test:bad","Provider":"test:missing",
                "VisualDiameter":2,"Layers":[{"Model":"cow","Texture":"/mob/cow.png"}]}}
                """)
        });
        var runtime = builder.Build();
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ClientEntityRendererRegistry().Build(runtime));
        Assert.Contains("omniblock:bad_impostor", error.Message);
        Assert.Contains("test:missing", error.Message);
    }

    [Fact]
    public void Unknown_impostor_model_rejects_the_candidate_client_catalog()
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddEntityDefinition(new EntityDefinition
        {
            Name = "bad_impostor_model", Namespace = Namespace.OmniBlock, ProtocolId = 20,
            Renderer = JsonSerializer.Deserialize<JsonElement>("""
                {"Type":"living","Model":"cow","Impostor":{"Id":"test:bad_model","Provider":"omniblock:basic",
                "VisualDiameter":2,"Layers":[{"Model":"definitely_missing","Texture":"/mob/cow.png"}]}}
                """)
        });
        var runtime = builder.Build();
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ClientEntityRendererRegistry().Build(runtime));
        Assert.Contains("omniblock:bad_impostor_model", error.Message);
        Assert.Contains("definitely_missing", error.Message);
    }
}
