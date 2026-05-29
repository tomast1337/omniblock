using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Entities.Models;
using BetaSharp.Entities;
using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities;

public class BoatEntityRenderer : EntityRenderer
{

    private readonly ModelBoat _modelBoat = new();

    public BoatEntityRenderer()
    {
        ShadowRadius = 0.5F;
    }

    public void render(EntityBoat boatEntity, double x, double y, double z, float yaw, float tickDelta)
    {
        GLManager.GL.PushMatrix();
        GLManager.GL.Translate((float)x, (float)y, (float)z);
        GLManager.GL.Rotate(180.0F - yaw, 0.0F, 1.0F, 0.0F);
        float timeSinceHit = boatEntity.BoatTimeSinceHit - tickDelta;
        float damageTaken = boatEntity.BoatCurrentDamage - tickDelta;
        if (damageTaken < 0.0F)
        {
            damageTaken = 0.0F;
        }

        if (timeSinceHit > 0.0F)
        {
            GLManager.GL.Rotate(MathHelper.Sin(timeSinceHit) * timeSinceHit * damageTaken / 10.0F * boatEntity.BoatRockDirection, 1.0F, 0.0F, 0.0F);
        }

        loadTexture("/terrain.png");
        float modelScale = 12.0F / 16.0F;
        GLManager.GL.Scale(modelScale, modelScale, modelScale);
        GLManager.GL.Scale(1.0F / modelScale, 1.0F / modelScale, 1.0F / modelScale);
        loadTexture("/item/boat.png");
        GLManager.GL.Scale(-1.0F, -1.0F, 1.0F);
        _modelBoat.Render(0.0F, 0.0F, -0.1F, 0.0F, 0.0F, 1.0F / 16.0F);
        GLManager.GL.PopMatrix();
    }

    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta)
    {
        render((EntityBoat)target, x, y, z, yaw, tickDelta);
    }
}
