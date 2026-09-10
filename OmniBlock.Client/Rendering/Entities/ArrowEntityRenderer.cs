using OmniBlock.Client.Rendering.Core;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Util.Maths;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Entities;

public class ArrowEntityRenderer : EntityRenderer
{
    public void renderArrow(Entity arrowEntity, double x, double y, double z, float yaw, float tickDelta)
    {
        if (arrowEntity.PrevYaw != 0.0F || arrowEntity.PrevPitch != 0.0F)
        {
            loadTexture("/item/arrows.png");
            RenderSystem.ModelView.Push();
            RenderSystem.ModelView.Translate((float)x, (float)y, (float)z);
            RenderSystem.ModelView.Rotate(arrowEntity.PrevYaw + (arrowEntity.Yaw - arrowEntity.PrevYaw) * tickDelta - 90.0F, 0.0F, 1.0F, 0.0F);
            RenderSystem.ModelView.Rotate(arrowEntity.PrevPitch + (arrowEntity.Pitch - arrowEntity.PrevPitch) * tickDelta, 0.0F, 0.0F, 1.0F);
            var tessellator = Tessellator.instance;
            byte arrowType = 0;
            var shaftMinU = 0.0F;
            var shaftMaxU = 0.5F;
            var featherMinV = (0 + arrowType * 10) / 32.0F;
            var featherMaxV = (5 + arrowType * 10) / 32.0F;
            var sideMinU = 0.0F;
            var sideMaxU = 0.15625F;
            var sideMinV = (5 + arrowType * 10) / 32.0F;
            var sideMaxV = (10 + arrowType * 10) / 32.0F;
            var modelScale = 0.05625F;
            var shakeTime = arrowEntity.Behaviors.Find<ArrowBehavior>()!.Shake(arrowEntity) - tickDelta;
            if (shakeTime > 0.0F)
            {
                var shakeRotation = -MathHelper.Sin(shakeTime * 3.0F) * shakeTime;
                RenderSystem.ModelView.Rotate(shakeRotation, 0.0F, 0.0F, 1.0F);
            }

            RenderSystem.ModelView.Rotate(45.0F, 1.0F, 0.0F, 0.0F);
            RenderSystem.ModelView.Scale(modelScale, modelScale, modelScale);
            RenderSystem.ModelView.Translate(-4.0F, 0.0F, 0.0F);
            RenderSystem.Normal = new Vector3D<float>(modelScale, 0.0F, 0.0F);
            tessellator.startDrawingQuads();
            tessellator.addVertexWithUV(-7.0D, -2.0D, -2.0D, sideMinU, sideMinV);
            tessellator.addVertexWithUV(-7.0D, -2.0D, 2.0D, sideMaxU, sideMinV);
            tessellator.addVertexWithUV(-7.0D, 2.0D, 2.0D, sideMaxU, sideMaxV);
            tessellator.addVertexWithUV(-7.0D, 2.0D, -2.0D, sideMinU, sideMaxV);
            tessellator.draw(ProgramSlot.Entities);
            RenderSystem.Normal = new Vector3D<float>(-modelScale, 0.0F, 0.0F);
            tessellator.startDrawingQuads();
            tessellator.addVertexWithUV(-7.0D, 2.0D, -2.0D, sideMinU, sideMinV);
            tessellator.addVertexWithUV(-7.0D, 2.0D, 2.0D, sideMaxU, sideMinV);
            tessellator.addVertexWithUV(-7.0D, -2.0D, 2.0D, sideMaxU, sideMaxV);
            tessellator.addVertexWithUV(-7.0D, -2.0D, -2.0D, sideMinU, sideMaxV);
            tessellator.draw(ProgramSlot.Entities);

            for (var quadIndex = 0; quadIndex < 4; ++quadIndex)
            {
                RenderSystem.ModelView.Rotate(90.0F, 1.0F, 0.0F, 0.0F);
                RenderSystem.Normal = new Vector3D<float>(0.0F, 0.0F, modelScale);
                tessellator.startDrawingQuads();
                tessellator.addVertexWithUV(-8.0D, -2.0D, 0.0D, shaftMinU, featherMinV);
                tessellator.addVertexWithUV(8.0D, -2.0D, 0.0D, shaftMaxU, featherMinV);
                tessellator.addVertexWithUV(8.0D, 2.0D, 0.0D, shaftMaxU, featherMaxV);
                tessellator.addVertexWithUV(-8.0D, 2.0D, 0.0D, shaftMinU, featherMaxV);
                tessellator.draw(ProgramSlot.Entities);
            }

            RenderSystem.ModelView.Pop();
        }
    }

    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta) => renderArrow(target, x, y, z, yaw, tickDelta);
}
