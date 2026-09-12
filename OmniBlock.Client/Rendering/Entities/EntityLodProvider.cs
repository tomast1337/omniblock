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
            EntityImpostorGeometry.BuildPoses(layer.Model))).ToArray();
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
            entity.IsOnFire || living.Health <= 0 || living.DeathTime != 0 || living.HeldItem != null ||
            living.GetTexture() != TexturePaths[0] || entity.Type?.Definition?.Scale != 1) return false;
        var body = EntityLodDirections.InterpolateYaw(living.LastBodyYaw, living.BodyYaw, partialTicks);
        var head = EntityLodDirections.InterpolateYaw(entity.PrevYaw, entity.Yaw, partialTicks);
        var pitch = entity.PrevPitch + (entity.Pitch - entity.PrevPitch) * partialTicks;
        return Math.Abs(EntityLodDirections.InterpolateYaw(body, head, 1) - body) < 0.01 && Math.Abs(pitch) < 0.01;
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
            EntityImpostorGeometry.BuildPoses(layer.Model))).ToArray();

    public bool Supports(Entity entity, float partialTicks)
    {
        if (entity is not EntityLiving living || entity.Dead || entity.HasVehicle || entity.Passenger != null ||
            entity.IsOnFire || living.Health <= 0 || living.DeathTime != 0 || living.HeldItem != null ||
            living.GetTexture() != TexturePaths[0] || entity.Type?.Definition?.Scale != 1 ||
            entity.Behaviors.Find<WoolBehavior>() is not { } wool || wool.ColorOf(entity) is < 0 or > 15) return false;
        var body = EntityLodDirections.InterpolateYaw(living.LastBodyYaw, living.BodyYaw, partialTicks);
        var head = EntityLodDirections.InterpolateYaw(entity.PrevYaw, entity.Yaw, partialTicks);
        var pitch = entity.PrevPitch + (entity.Pitch - entity.PrevPitch) * partialTicks;
        return Math.Abs(EntityLodDirections.InterpolateYaw(body, head, 1) - body) < 0.01 && Math.Abs(pitch) < 0.01;
    }

    public int Pose(Entity entity, float partialTicks) => entity is EntityLiving living
        ? BasicEntityImpostorProvider.SelectPose(living.LastWalkAnimationSpeed, living.WalkAnimationSpeed,
            living.AnimationPhase, partialTicks)
        : 0;

    public Vector4 LayerEffects(Entity entity, float partialTicks)
    {
        if (entity.Behaviors.Find<WoolBehavior>() is not { } wool) return new Vector4(1, 1, 1, 0);
        var tint = WoolBehavior.ColorTable[wool.ColorOf(entity)];
        return new Vector4(tint[0], tint[1], tint[2], wool.IsShearedOn(entity) ? 0 : 1);
    }
}
