using System.Collections.Frozen;
using System.Text.Json;
using OmniBlock.Client.Rendering.Entities.Models;
using OmniBlock.Client.Rendering.Items;
using OmniBlock.Entities;
using OmniBlock.Registries;

namespace OmniBlock.Client.Rendering.Entities;

internal interface IClientEntityRendererProvider
{
    EntityRenderer Build(EntityRenderDescriptor descriptor, ContentRuntime content);
}

/// <summary>Client-only provider catalog and compiled renderer snapshot for one content runtime.</summary>
internal sealed class ClientEntityRendererRegistry
{
    private readonly Dictionary<ResourceLocation, IClientEntityRendererProvider> _providers = [];

    public ClientEntityRendererRegistry()
    {
        Register("living", (d, _) =>
        {
            var model = Model(d);
            return new LivingEntityRenderer(model, Shadow(d))
            {
                // Explicit client provider opt-in. Fleece, players and custom renderers stay 3D.
                LodProvider = model is ModelCow ? new CowImpostorProvider() : null
            };
        });
        Register("flapping", (d, _) => new FlappingEntityRenderer(Model(d), Shadow(d)));
        Register("glowing_eyes", (d, _) => new GlowingEyesEntityRenderer(
            Model(d), EntityModelRegistry.Create(Json(d).GetProperty("OverlayModel").GetString()!), Shadow(d),
            Json(d).GetProperty("OverlayTexture").GetString()!,
            Json(d).TryGetProperty("DeathRotation", out var death) ? death.GetSingle() : 90.0F));
        Register("charging", (d, _) => new ChargingEntityRenderer(Model(d), Shadow(d)));
        Register("swimming", (d, _) => new SwimmingEntityRenderer(Model(d), Shadow(d)));
        Register("tamed", (d, _) => new TamedEntityRenderer(Model(d), Shadow(d)));
        Register("squishy", (d, _) => new SquishyEntityRenderer(
            Model(d), EntityModelRegistry.Create(Json(d).GetProperty("OverlayModel").GetString()!), Shadow(d)));
        Register("falling_block", (d, _) => new FallingBlockEntityRenderer(Shadow(d)));
        Register("lightning", (_, _) => new LightningEntityRenderer());
        Register("item", (_, content) => new ItemRenderer(content.Blocks));
        Register("arrow", (_, _) => new ArrowEntityRenderer());
        Register("painting", (_, _) => new PaintingEntityRenderer());
        Register("fishing_bobber", (_, _) => new FishingBobberEntityRenderer());
        Register("boat", (_, _) => new BoatEntityRenderer());
        Register("minecart", (_, _) => new MinecartEntityRenderer());
        Register("projectile", (d, content) =>
        {
            var item = ResourceLocation.Parse(Json(d).GetProperty("Item").GetString()!);
            _ = content.Items.Get(item);
            return new ProjectileEntityRenderer(item,
                Json(d).TryGetProperty("Scale", out var scale) ? scale.GetSingle() : 0.5F);
        });
        Register("primed_block", (d, content) => new PrimedBlockEntityRenderer(
            content.Blocks.Get(Json(d).GetProperty("Block").GetString()!), Shadow(d)));
        Register("scaled", (d, _) => new ScaledEntityRenderer(
            Model(d), Shadow(d), Json(d).GetProperty("Scale").GetSingle()));
        Register("undead", (d, _) => new UndeadEntityRenderer((ModelBiped)Model(d), Shadow(d)));
        Register("creeper", (d, _) => new FuseEntityRenderer(
            Model(d), EntityModelRegistry.Create(Json(d).GetProperty("OverlayModel").GetString()!), Shadow(d),
            Json(d).GetProperty("OverlayTexture").GetString()!,
            Json(d).GetProperty("OverlayProperty").GetString()!));
        Register("fleece", (d, _) => new FleeceEntityRenderer(
            Model(d), EntityModelRegistry.Create(Json(d).GetProperty("OverlayModel").GetString()!), Shadow(d),
            Json(d).GetProperty("OverlayTexture").GetString()!));
        Register("overlay", (d, _) => new OverlayEntityRenderer(
            Model(d), EntityModelRegistry.Create(Json(d).GetProperty("OverlayModel").GetString()!), Shadow(d),
            Json(d).GetProperty("OverlayProperty").GetString()!,
            Json(d).GetProperty("OverlayTexture").GetString()!));
    }

    public FrozenDictionary<EntityType, EntityRenderer> Build(ContentRuntime content)
    {
        var renderers = new Dictionary<EntityType, EntityRenderer>();
        // Resolve the complete provider set first so a bad client mod cannot leave model/render
        // registrations half constructed before the catalog is rejected.
        foreach (var key in content.EntityTypes.Keys)
        {
            var descriptor = content.EntityTypes.Get(key).RenderDescriptor;
            if (descriptor is not null && !_providers.ContainsKey(descriptor.ProviderType))
            {
                throw CatalogError(key, descriptor.ProviderType,
                    $"Unknown client entity renderer provider '{descriptor.ProviderType}'.");
            }
        }

        foreach (var key in content.EntityTypes.Keys)
        {
            var entityType = content.EntityTypes.Get(key);
            if (entityType.RenderDescriptor is not { } descriptor) continue;
            try
            {
                var provider = _providers[descriptor.ProviderType];
                renderers.Add(entityType, provider.Build(descriptor, content));
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(
                    $"Client entity catalog rejected entity '{key}' renderer '{descriptor.ProviderType}': {error.Message}",
                    error);
            }
        }

        return renderers.ToFrozenDictionary();
    }

    private static InvalidOperationException CatalogError(
        ResourceLocation owner, ResourceLocation provider, string message) =>
        new($"Client entity catalog rejected entity '{owner}' renderer '{provider}': {message}");

    internal void Register(ResourceLocation type, IClientEntityRendererProvider provider)
    {
        if (!_providers.TryAdd(type, provider))
            throw new ArgumentException($"Duplicate client entity renderer provider '{type}'.");
    }

    private void Register(string path, Func<EntityRenderDescriptor, ContentRuntime, EntityRenderer> factory) =>
        Register(new ResourceLocation(Namespace.OmniBlock, path), new DelegateProvider(factory));

    private static JsonElement Json(EntityRenderDescriptor descriptor) => descriptor.Definition;

    private static ModelBase Model(EntityRenderDescriptor descriptor) =>
        EntityModelRegistry.Create(Json(descriptor).GetProperty("Model").GetString()
                                   ?? throw new ArgumentException("Entity renderer is missing its 'Model' name."));

    private static float Shadow(EntityRenderDescriptor descriptor) =>
        Json(descriptor).TryGetProperty("Shadow", out var shadow) ? shadow.GetSingle() : 0.0F;

    private sealed class DelegateProvider(
        Func<EntityRenderDescriptor, ContentRuntime, EntityRenderer> factory) : IClientEntityRendererProvider
    {
        public EntityRenderer Build(EntityRenderDescriptor descriptor, ContentRuntime content) =>
            factory(descriptor, content);
    }
}
