using System.Numerics;
using OmniBlock.Client.Rendering.Entities.Models;
using OmniBlock.Entities;

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

internal sealed class StandingCowLodProvider : IEntityLodProvider
{
    public ResourceLocation Id { get; } = new(Namespace.OmniBlock, "standing_cow");
    public double VisualDiameter { get; }
    public string VariantKey => "omniblock:standing_cow:v1";

    public StandingCowLodProvider()
    {
        // Same cached geometry and conversion as ModelCow, without constructing/touching a live
        // model or GPU buffers. Enclose all standing-pose corners in a sphere about entity origin;
        // this deliberately overestimates projected height, including horns and modified models.
        var document = BbModelLoader.LoadCached("cow");
        var radius = 0.0;
        foreach (var group in document.Groups.Where(g => g.Export))
        {
            var entry = document.Outliner.FirstOrDefault(e => e.Uuid == group.Uuid);
            var element = document.Elements.FirstOrDefault(e => e.Uuid == entry?.Children.FirstOrDefault());
            if (element == null || !element.Export || element.Type != "cube") continue;
            var part = BbModelModelBuilder.ConvertElement(group, element, 0);
            for (var corner = 0; corner < 8; corner++)
            {
                var vertex = new Vector3(
                    part.BoxX + ((corner & 1) == 0 ? -part.Inflate : part.SizeX + part.Inflate),
                    part.BoxY + ((corner & 2) == 0 ? -part.Inflate : part.SizeY + part.Inflate),
                    part.BoxZ + ((corner & 4) == 0 ? -part.Inflate : part.SizeZ + part.Inflate));
                if (part.Name is "body" or "udders") vertex = new Vector3(vertex.X, -vertex.Z, vertex.Y);
                vertex += new Vector3(part.PivotX, part.PivotY - 24, part.PivotZ);
                radius = Math.Max(radius, vertex.Length() / 16.0 + 1.0 / 128);
            }
        }
        VisualDiameter = radius * 2;
    }

    public bool Supports(Entity entity, float partialTicks)
    {
        if (entity is not EntityLiving living || entity.Dead || entity.HasVehicle || entity.Passenger != null ||
            entity.IsOnFire || living.Health <= 0 || living.DeathTime != 0 || living.HeldItem != null ||
            living.GetTexture() != "/mob/cow.png" || entity.Type?.Definition?.Scale != 1) return false;
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
