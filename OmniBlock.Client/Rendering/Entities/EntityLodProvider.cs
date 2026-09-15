using System.Numerics;
using OmniBlock.Client.Rendering.Entities.Models;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>Client renderer opt-in, separate from gameplay/entity registration and future atlas availability.</summary>
internal interface IEntityLodProvider
{
    ResourceLocation Id { get; }
    double VisualDiameter { get; }
    string VariantKey { get; }
    bool Supports(Entity entity, float partialTicks);
    int Pose(Entity entity, float partialTicks);
    float Scale(Entity entity);
}

internal interface IEntityImpostorProvider : IEntityLodProvider
{
    string CacheIdentity { get; }
    IReadOnlyList<string> TexturePaths { get; }
    EntityImpostorCaptureLayer[] BuildLayers();
    Vector4 LayerEffects(Entity entity, float partialTicks);
}

internal sealed class BasicEntityImpostorProvider : IEntityImpostorProvider
{
    private readonly ClientEntityImpostorDescriptor _descriptor;
    public ResourceLocation Id => _descriptor.Id;
    public double VisualDiameter => _descriptor.VisualDiameter;
    public string VariantKey => $"{Id}:basic-v1";
    public IReadOnlyList<string> TexturePaths { get; }
    public string CacheIdentity => "omniblock:basic-impostor-v1:root24:RGBA8:cutout0.1:nearest:mip0";
    public EntityImpostorCaptureLayer[] BuildLayers() => _descriptor.Layers
        .Select(layer => new EntityImpostorCaptureLayer(layer.Texture,
            EntityImpostorGeometry.BuildPoses(layer.Model, layer.PoseProvider))).ToArray();
    public Vector4 LayerEffects(Entity entity, float partialTicks) => new(1, 1, 1, 0);

    public BasicEntityImpostorProvider(ClientEntityImpostorDescriptor descriptor)
    {
        if (descriptor.Layers.Length != 1)
            throw new InvalidDataException($"Basic impostor '{descriptor.Id}' requires exactly one layer.");
        _descriptor = descriptor;
        TexturePaths = descriptor.Layers.Select(layer => layer.Texture).ToArray();
    }

    public bool Supports(Entity entity, float partialTicks)
    {
        if (entity is not EntityLiving living || entity.Dead || entity.HasVehicle || entity.Passenger != null ||
            entity.IsOnFire || living.Health <= 0 || living.DeathTime != 0 ||
            (!_descriptor.OmitHeldItem && living.HeldItem != null) ||
            living.GetTexture() != TexturePaths[0] || !float.IsFinite(Scale(entity)) || Scale(entity) <= 0 ||
            (_descriptor.UnsupportedWhenTrue is { } property &&
             entity.Synced<bool>(property)?.Value != false)) return false;
        // Head yaw/pitch is deliberately reduced to the captured pose at this LOD. Requiring exact
        // alignment made naturally spawned animals almost permanently ineligible: AI commonly
        // leaves the head turned when its distant simulation pauses, unlike the canonical E2E mob.
        return true;
    }

    public float Scale(Entity entity)
    {
        var declared = entity.Type?.Definition?.Scale ?? 1;
        if (_descriptor.ScaleProperty is not { } property) return declared;
        return declared * (entity.Synced<byte>(property)?.Value ?? 0);
    }

    public int Pose(Entity entity, float partialTicks)
    {
        if (entity is not EntityLiving living) return 0;
        return SelectPose(living.LastWalkAnimationSpeed, living.WalkAnimationSpeed,
            living.AnimationPhase, partialTicks);
    }

    internal static int SelectPose(float lastAmount, float amountNow, float animationPhase, float partialTicks)
    {
        var amount = lastAmount + (amountNow - lastAmount) * partialTicks;
        if (Math.Abs(amount) < 0.05f) return 0;
        var phase = animationPhase - amountNow * (1 - partialTicks);
        var cycle = phase * 0.6662f / (MathF.PI * 2);
        cycle -= MathF.Floor(cycle);
        return 1 + ((int)MathF.Floor(cycle * 4 + 0.5f) & 3);
    }
}

/// <summary>Basic silhouette plus the creeper states that require its animated/charged 3D renderer.</summary>
internal sealed class CreeperImpostorProvider : IEntityImpostorProvider
{
    private readonly BasicEntityImpostorProvider _basic;
    public CreeperImpostorProvider(ClientEntityImpostorDescriptor descriptor) =>
        _basic = new BasicEntityImpostorProvider(descriptor);
    public ResourceLocation Id => _basic.Id;
    public double VisualDiameter => _basic.VisualDiameter;
    public string VariantKey => $"{Id}:creeper-v1";
    public string CacheIdentity => "omniblock:creeper-impostor-v1";
    public IReadOnlyList<string> TexturePaths => _basic.TexturePaths;
    public EntityImpostorCaptureLayer[] BuildLayers() => _basic.BuildLayers();
    public Vector4 LayerEffects(Entity entity, float partialTicks) => _basic.LayerEffects(entity, partialTicks);
    public int Pose(Entity entity, float partialTicks) => _basic.Pose(entity, partialTicks);
    public float Scale(Entity entity) => _basic.Scale(entity);
    public bool Supports(Entity entity, float partialTicks) =>
        _basic.Supports(entity, partialTicks) &&
        entity.Synced<bool>("powered")?.Value == false &&
        entity.Synced<byte>("state")?.Value == byte.MaxValue;
}

/// <summary>
/// Uses the canonical standing wild-wolf capture. Tamed, angry, sitting, and shaking wolves retain
/// their 3D renderer because those states change the texture or silhouette.
/// </summary>
internal sealed class WolfImpostorProvider : IEntityImpostorProvider
{
    private readonly BasicEntityImpostorProvider _basic;
    public WolfImpostorProvider(ClientEntityImpostorDescriptor descriptor) =>
        _basic = new BasicEntityImpostorProvider(descriptor);
    public ResourceLocation Id => _basic.Id;
    public double VisualDiameter => _basic.VisualDiameter;
    public string VariantKey => $"{Id}:wild-wolf-v1";
    public string CacheIdentity => "omniblock:wild-wolf-impostor-v1";
    public IReadOnlyList<string> TexturePaths => _basic.TexturePaths;
    public EntityImpostorCaptureLayer[] BuildLayers() => _basic.BuildLayers();
    public Vector4 LayerEffects(Entity entity, float partialTicks) => _basic.LayerEffects(entity, partialTicks);
    public int Pose(Entity entity, float partialTicks) => _basic.Pose(entity, partialTicks);
    public float Scale(Entity entity) => _basic.Scale(entity);

    public bool Supports(Entity entity, float partialTicks)
    {
        if (!_basic.Supports(entity, partialTicks) ||
            entity.Behaviors.Find<TameableBehavior>() is not { } tame)
            return false;
        return !tame.IsTamed(entity) && !tame.IsAngry(entity) && !tame.IsSitting(entity) &&
               entity.Behaviors.Find<ShakeOffWaterBehavior>()?.IsShaking(entity) != true;
    }
}

internal sealed class SheepImpostorProvider : IEntityImpostorProvider
{
    private readonly ClientEntityImpostorDescriptor _descriptor;
    public ResourceLocation Id => _descriptor.Id;
    public double VisualDiameter => _descriptor.VisualDiameter;
    public string VariantKey => $"{Id}:wool-v1";
    public string CacheIdentity => "omniblock:sheep:layered-v1:RGBA8:cutout0.1:nearest:mip0";
    public IReadOnlyList<string> TexturePaths { get; }

    public SheepImpostorProvider(ClientEntityImpostorDescriptor descriptor)
    {
        if (descriptor.Layers.Length != 2)
            throw new InvalidDataException($"Wool impostor '{descriptor.Id}' requires base and fleece layers.");
        _descriptor = descriptor;
        TexturePaths = descriptor.Layers.Select(layer => layer.Texture).ToArray();
    }

    public EntityImpostorCaptureLayer[] BuildLayers() => _descriptor.Layers
        .Select(layer => new EntityImpostorCaptureLayer(layer.Texture,
            EntityImpostorGeometry.BuildPoses(layer.Model, layer.PoseProvider))).ToArray();

    public bool Supports(Entity entity, float partialTicks)
    {
        if (entity is not EntityLiving living || entity.Dead || entity.HasVehicle || entity.Passenger != null ||
            entity.IsOnFire || living.Health <= 0 || living.DeathTime != 0 || living.HeldItem != null ||
            living.GetTexture() != TexturePaths[0] || entity.Type?.Definition?.Scale != 1 ||
            entity.Behaviors.Find<WoolBehavior>() is not { } wool || wool.ColorOf(entity) is < 0 or > 15) return false;
        // As with the basic provider, normal head motion is detail-reduced rather than treated as
        // an unsupported state. Wool color and shearing still remain exact per-instance state.
        return true;
    }

    public int Pose(Entity entity, float partialTicks) => entity is EntityLiving living
        ? BasicEntityImpostorProvider.SelectPose(living.LastWalkAnimationSpeed, living.WalkAnimationSpeed,
            living.AnimationPhase, partialTicks)
        : 0;

    public float Scale(Entity entity) => entity.Type?.Definition?.Scale ?? 1;

    public Vector4 LayerEffects(Entity entity, float partialTicks)
    {
        if (entity.Behaviors.Find<WoolBehavior>() is not { } wool) return new Vector4(1, 1, 1, 0);
        var tint = WoolBehavior.ColorTable[wool.ColorOf(entity)];
        return new Vector4(tint[0], tint[1], tint[2], wool.IsShearedOn(entity) ? 0 : 1);
    }
}
