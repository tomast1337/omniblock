using System.Text.Json;
using OmniBlock.Client.Rendering.Entities;
using OmniBlock.Entities;
using OmniBlock.Registries;

namespace OmniBlock.Tests.Entities;

[Collection("EntityTests")]
public sealed class ClientEntityRendererRegistryTests
{
    [Fact]
    public void Shared_runtime_preserves_a_namespaced_immutable_render_descriptor()
    {
        EntityType cow = ContentRuntime.Current.EntityTypes.Get("omniblock:cow");

        EntityRenderDescriptor descriptor = Assert.IsType<EntityRenderDescriptor>(cow.RenderDescriptor);
        Assert.Equal(ResourceLocation.Parse("omniblock:living"), descriptor.ProviderType);
        Assert.Equal("cow", descriptor.Definition.GetProperty("Model").GetString());
    }

    [Fact]
    public void Unknown_client_provider_identifies_the_entity_and_provider()
    {
        ContentRuntime published = ContentRuntime.Current;
        ContentRuntimeBuilder builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddEntityDefinition(new EntityDefinition
        {
            Name = "client_only_failure",
            Namespace = Namespace.OmniBlock,
            ProtocolId = 20,
            Renderer = JsonSerializer.Deserialize<JsonElement>("""{"Type":"example:missing_renderer"}""")
        });
        ContentRuntime runtime = builder.Build();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new ClientEntityRendererRegistry().Build(runtime));

        Assert.Contains("omniblock:client_only_failure", error.Message);
        Assert.Contains("example:missing_renderer", error.Message);
        Assert.Same(published, ContentRuntime.Current);
    }
}
