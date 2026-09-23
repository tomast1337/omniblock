using System.Text.Json;
using OmniBlock.Client.Rendering.Entities;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;

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
        IEnumerable<string> dependencies =
            new ClientEntityRendererRegistry().CaptureDependencies(ContentRuntime.Current);

        Assert.Equal(14, dependencies.Count());
        Assert.Contains("/mob/cow.png", dependencies);
        Assert.Contains("/mob/chicken.png", dependencies);
        Assert.Contains("/mob/creeper.png", dependencies);
        Assert.Contains("/mob/ghast.png", dependencies);
        Assert.Contains("/mob/pig.png", dependencies);
        Assert.Contains("/mob/pigzombie.png", dependencies);
        Assert.Contains("/mob/sheep.png", dependencies);
        Assert.Contains("/mob/sheep_fur.png", dependencies);
        Assert.Contains("/mob/skeleton.png", dependencies);
        Assert.Contains("/mob/slime.png", dependencies);
        Assert.Contains("/mob/spider.png", dependencies);
        Assert.Contains("/mob/squid.png", dependencies);
        Assert.Contains("/mob/zombie.png", dependencies);
        Assert.Contains("/mob/wolf.png", dependencies);
    }

    [Fact]
    public void Every_shipped_living_mob_has_a_compiled_impostor_provider()
    {
        string[] expected =
        [
            "chicken", "cow", "creeper", "ghast", "giant", "pig", "pigzombie", "sheep",
            "skeleton", "slime", "spider", "squid", "wolf", "zombie"
        ];
        var runtime = ContentRuntime.Current;
        var renderers = new ClientEntityRendererRegistry();

        foreach (var name in expected)
        {
            var type = runtime.EntityTypes.Get("omniblock:" + name);
            Assert.NotNull(ClientEntityImpostorDescriptor.Compile(type.RenderDescriptor!.Definition));
            Assert.IsAssignableFrom<IEntityImpostorProvider>(
                renderers.CompileImpostorProvider(type.RenderDescriptor));
        }
    }

    [Fact]
    public void Wild_wolf_uses_an_impostor_but_texture_and_pose_changing_states_keep_3d()
    {
        var wolfType = ContentRuntime.Current.EntityTypes.Get("omniblock:wolf");
        var descriptor = Assert.IsType<ClientEntityImpostorDescriptor>(
            ClientEntityImpostorDescriptor.Compile(wolfType.RenderDescriptor!.Definition));
        var provider = new WolfImpostorProvider(descriptor);
        var wolf = wolfType.Create(new FakeWorldContext());
        var tame = Assert.IsType<TameableBehavior>(wolf.Behaviors.Find<TameableBehavior>());

        Assert.True(provider.Supports(wolf, 0));
        tame.SetSitting(wolf, true);
        Assert.False(provider.Supports(wolf, 0));
    }

    [Fact]
    public void Hostile_impostors_cover_equipped_skeletons_while_creeper_special_states_keep_3d()
    {
        var runtime = ContentRuntime.Current;
        var zombieType = runtime.EntityTypes.Get("omniblock:zombie");
        var creeperType = runtime.EntityTypes.Get("omniblock:creeper");
        var skeletonType = runtime.EntityTypes.Get("omniblock:skeleton");
        var zombie = ClientEntityImpostorDescriptor.Compile(zombieType.RenderDescriptor!.Definition);
        var creeperDescriptor = ClientEntityImpostorDescriptor.Compile(creeperType.RenderDescriptor!.Definition);
        Assert.NotNull(zombie);
        var provider = new CreeperImpostorProvider(Assert.IsType<ClientEntityImpostorDescriptor>(creeperDescriptor));
        var skeletonDescriptor = Assert.IsType<ClientEntityImpostorDescriptor>(
            ClientEntityImpostorDescriptor.Compile(skeletonType.RenderDescriptor!.Definition));
        Assert.True(skeletonDescriptor.OmitHeldItem);
        var skeleton = skeletonType.Create(new FakeWorldContext());
        Assert.NotNull(((EntityLiving)skeleton).HeldItem);
        Assert.True(new BasicEntityImpostorProvider(skeletonDescriptor).Supports(skeleton, 0));

        var creeper = creeperType.Create(new FakeWorldContext());
        Assert.True(provider.Supports(creeper, 0));
        creeper.Synced<bool>("powered")!.Value = true;
        Assert.False(provider.Supports(creeper, 0));
        creeper.Synced<bool>("powered")!.Value = false;
        creeper.Synced<byte>("state")!.Value = 1;
        Assert.False(provider.Supports(creeper, 0));
    }

    [Fact]
    public void Declared_visual_state_and_scale_rules_are_enforced_by_the_basic_provider()
    {
        var runtime = ContentRuntime.Current;
        var pigType = runtime.EntityTypes.Get("omniblock:pig");
        var pigDescriptor = Assert.IsType<ClientEntityImpostorDescriptor>(
            ClientEntityImpostorDescriptor.Compile(pigType.RenderDescriptor!.Definition));
        var pigProvider = new BasicEntityImpostorProvider(pigDescriptor);
        var pig = pigType.Create(new FakeWorldContext());
        Assert.True(pigProvider.Supports(pig, 0));
        pig.Synced<bool>("saddled")!.Value = true;
        Assert.False(pigProvider.Supports(pig, 0));

        var giantType = runtime.EntityTypes.Get("omniblock:giant");
        var giantProvider = new BasicEntityImpostorProvider(Assert.IsType<ClientEntityImpostorDescriptor>(
            ClientEntityImpostorDescriptor.Compile(giantType.RenderDescriptor!.Definition)));
        Assert.Equal(6, giantProvider.Scale(giantType.Create(new FakeWorldContext())));

        var slimeType = runtime.EntityTypes.Get("omniblock:slime");
        var slimeProvider = new BasicEntityImpostorProvider(Assert.IsType<ClientEntityImpostorDescriptor>(
            ClientEntityImpostorDescriptor.Compile(slimeType.RenderDescriptor!.Definition)));
        var slime = slimeType.Create(new FakeWorldContext());
        slime.Synced<byte>("size")!.Value = 4;
        Assert.Equal(4, slimeProvider.Scale(slime));
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
