using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Entities;
using OmniBlock.Entities;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;
using Silk.NET.Maths;

namespace OmniBlock.Client.Entities.FX;

public class EntityPickupFX : EntityFX
{
    private readonly int maxAge;
    private readonly Entity source;

    private readonly Entity target;
    private readonly float yOffset;
    private int currentAge;

    public EntityPickupFX(IWorldContext world, Entity target, Entity source, float yOffset) : base(world, target.X, target.Y, target.Z, target.VelocityX, target.VelocityY, target.VelocityZ)
    {
        this.target = target;
        this.source = source;
        this.yOffset = yOffset;
        maxAge = 3;
    }

    public override void renderParticle(Tessellator t, float partialTick, float rotX, float rotY, float rotZ, float upX, float upZ)
    {
        var lifeProgress = (currentAge + partialTick) / maxAge;
        lifeProgress *= lifeProgress;
        var targetX = target.X;
        var targetY = target.Y;
        var targetZ = target.Z;
        var sourceX = source.LastTickX + (source.X - source.LastTickX) * partialTick;
        var sourceY = source.LastTickY + (source.Y - source.LastTickY) * partialTick + yOffset;
        var sourceZ = source.LastTickZ + (source.Z - source.LastTickZ) * partialTick;
        var renderX = targetX + (sourceX - targetX) * lifeProgress;
        var renderY = targetY + (sourceY - targetY) * lifeProgress;
        var renderZ = targetZ + (sourceZ - targetZ) * lifeProgress;
        var itemX = MathHelper.Floor(renderX);
        var itemY = MathHelper.Floor(renderY + StandingEyeHeight / 2.0F);
        var itemZ = MathHelper.Floor(renderZ);
        var luminance = World.Lighting.GetLuminance(itemX, itemY, itemZ);
        renderX -= interpPosX;
        renderY -= interpPosY;
        renderZ -= interpPosZ;
        GLManager.Color = new Vector4D<float>(luminance, luminance, luminance, 1.0F);
        EntityRenderDispatcher.Instance.RenderEntityWithPosYaw(target, (float)renderX, (float)renderY, (float)renderZ, target.Yaw, partialTick);
    }

    public override void Tick()
    {
        ++currentAge;
        if (currentAge == maxAge)
        {
            MarkDead();
        }
    }

    public override int getFXLayer() => 3;
}
