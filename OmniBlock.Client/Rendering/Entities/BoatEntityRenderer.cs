using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Entities.Models;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Entities;

public class BoatEntityRenderer : EntityRenderer
{
    private readonly ModelBoat _modelBoat = new();

    public BoatEntityRenderer() => ShadowRadius = 0.5F;

    public void render(Entity boatEntity, double x, double y, double z, float yaw, float tickDelta)
    {
        var hull = boatEntity.Behaviors.Find<BoatBehavior>()!;
        GLManager.ModelView.Push();
        GLManager.ModelView.Translate((float)x, (float)y, (float)z);
        GLManager.ModelView.Rotate(180.0F - yaw, 0.0F, 1.0F, 0.0F);
        var timeSinceHit = hull.TimeSinceHit(boatEntity) - tickDelta;
        var damageTaken = hull.Damage(boatEntity) - tickDelta;
        if (damageTaken < 0.0F)
        {
            damageTaken = 0.0F;
        }

        if (timeSinceHit > 0.0F)
        {
            GLManager.ModelView.Rotate(MathHelper.Sin(timeSinceHit) * timeSinceHit * damageTaken / 10.0F * hull.RockDirection(boatEntity), 1.0F, 0.0F, 0.0F);
        }

        loadTexture("/terrain.png");
        var modelScale = 12.0F / 16.0F;
        GLManager.ModelView.Scale(modelScale, modelScale, modelScale);
        GLManager.ModelView.Scale(1.0F / modelScale, 1.0F / modelScale, 1.0F / modelScale);
        loadTexture("/item/boat.png");
        GLManager.ModelView.Scale(-1.0F, -1.0F, 1.0F);
        _modelBoat.Render(0.0F, 0.0F, -0.1F, 0.0F, 0.0F, 1.0F / 16.0F);
        GLManager.ModelView.Pop();
    }

    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta) => render(target, x, y, z, yaw, tickDelta);
}
