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

        Assert.Equal(5, dependencies.Count);
        Assert.True(dependencies.Contains("/mob/cow.png"));
        Assert.True(dependencies.Contains("/mob/creeper.png"));
        Assert.True(dependencies.Contains("/mob/sheep.png"));
        Assert.True(dependencies.Contains("/mob/sheep_fur.png"));
        Assert.True(dependencies.Contains("/mob/zombie.png"));
    }

    [Fact]
    public void Hostile_impostors_are_opt_in_and_creeper_special_states_keep_the_3d_renderer()
    {
        var runtime = ContentRuntime.Current;
        var zombieType = runtime.EntityTypes.Get("omniblock:zombie");
        var creeperType = runtime.EntityTypes.Get("omniblock:creeper");
        var skeletonType = runtime.EntityTypes.Get("omniblock:skeleton");
        var zombie = ClientEntityImpostorDescriptor.Compile(zombieType.RenderDescriptor!.Definition);
        var creeperDescriptor = ClientEntityImpostorDescriptor.Compile(creeperType.RenderDescriptor!.Definition);
        Assert.NotNull(zombie);
        var provider = new CreeperImpostorProvider(Assert.IsType<ClientEntityImpostorDescriptor>(creeperDescriptor));
        Assert.Null(ClientEntityImpostorDescriptor.Compile(skeletonType.RenderDescriptor!.Definition));
        // A skeleton's held bow is not represented yet.

        var creeper = creeperType.Create(new FakeWorldContext());
        Assert.True(provider.Supports(creeper, 0));
        creeper.Synced<bool>("powered")!.Value = true;
        Assert.False(provider.Supports(creeper, 0));
        creeper.Synced<bool>("powered")!.Value = false;
        creeper.Synced<byte>("state")!.Value = 1;
        Assert.False(provider.Supports(creeper, 0));
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
                "VisualDiameter":2,"Layers":[{"Model":"cow","Texture":"/mob/cow.png","PoseProvider":"omniblock:quadruped"}]}}
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
                "VisualDiameter":2,"Layers":[{"Model":"definitely_missing","Texture":"/mob/cow.png","PoseProvider":"omniblock:quadruped"}]}}
                """)
        });
        var runtime = builder.Build();
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ClientEntityRendererRegistry().Build(runtime));
        Assert.Contains("omniblock:bad_impostor_model", error.Message);
        Assert.Contains("definitely_missing", error.Message);
    }

    [Fact]
    public void Unknown_impostor_pose_provider_rejects_the_candidate_client_catalog()
    {
        var builder = ContentRuntimeBuilder.CreateBuiltIns();
        builder.AddEntityDefinition(new EntityDefinition
        {
            Name = "bad_impostor_pose", Namespace = Namespace.OmniBlock, ProtocolId = 20,
            Renderer = JsonSerializer.Deserialize<JsonElement>("""
                {"Type":"living","Model":"cow","Impostor":{"Id":"test:bad_pose","Provider":"omniblock:basic",
                "VisualDiameter":2,"Layers":[{"Model":"cow","Texture":"/mob/cow.png","PoseProvider":"test:missing"}]}}
                """)
        });
        var runtime = builder.Build();
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ClientEntityRendererRegistry().CaptureDependencies(runtime));
        Assert.Contains("omniblock:bad_impostor_pose", error.Message);
        Assert.Contains("test:missing", error.Message);
    }
}
