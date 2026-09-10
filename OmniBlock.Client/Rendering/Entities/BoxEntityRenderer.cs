using OmniBlock.Client.Rendering.Core;
using OmniBlock.Entities;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Entities;

public class BoxEntityRenderer : EntityRenderer
{
    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta)
    {
        RenderSystem.ModelView.Push();
        renderShape(target.BoundingBox, new Vec3D(x - target.LastTickX, y - target.LastTickY, z - target.LastTickZ));
        RenderSystem.ModelView.Pop();
    }
}
