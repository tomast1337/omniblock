using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Entities;

public class FishingBobberEntityRenderer : EntityRenderer
{
    public void render(Entity bobberEntity, double x, double y, double z, float yaw, float tickDelta)
    {
        var angler = bobberEntity.Behaviors.Find<FishingBobberBehavior>()!.Angler(bobberEntity);
        RenderSystem.ModelView.Push();
        RenderSystem.ModelView.Translate((float)x, (float)y, (float)z);
        RenderSystem.ModelView.Scale(0.5F, 0.5F, 0.5F);
        byte particleUIndex = 1;
        byte particleVIndex = 2;
        loadTexture("/particles.png");
        var tessellator = Tessellator.instance;
        var minU = (particleUIndex * 8 + 0) / 128.0F;
        var maxU = (particleUIndex * 8 + 8) / 128.0F;
        var minV = (particleVIndex * 8 + 0) / 128.0F;
        var maxV = (particleVIndex * 8 + 8) / 128.0F;
        var quadWidth = 1.0F;
        var xOffset = 0.5F;
        var yOffset = 0.5F;
        RenderSystem.ModelView.Rotate(180.0F - Dispatcher.PlayerViewY, 0.0F, 1.0F, 0.0F);
        RenderSystem.ModelView.Rotate(-Dispatcher.PlayerViewX, 1.0F, 0.0F, 0.0F);
        tessellator.startDrawingQuads();
        tessellator.setNormal(0.0F, 1.0F, 0.0F);
        tessellator.addVertexWithUV(0.0F - xOffset, 0.0F - yOffset, 0.0D, minU, maxV);
        tessellator.addVertexWithUV(quadWidth - xOffset, 0.0F - yOffset, 0.0D, maxU, maxV);
        tessellator.addVertexWithUV(quadWidth - xOffset, 1.0F - yOffset, 0.0D, maxU, minV);
        tessellator.addVertexWithUV(0.0F - xOffset, 1.0F - yOffset, 0.0D, minU, minV);
        tessellator.draw(ProgramSlot.Entities);
        RenderSystem.ModelView.Pop();
        if (angler != null)
        {
            var anglerYawRadians = (angler.PrevYaw + (angler.Yaw - angler.PrevYaw) * tickDelta) * (float)Math.PI / 180.0F;
            var sinYaw = (double)MathHelper.Sin(anglerYawRadians);
            var cosYaw = (double)MathHelper.Cos(anglerYawRadians);
            var swingProgress = angler.GetSwingProgress(tickDelta);
            var swingOffset = MathHelper.Sin(MathHelper.Sqrt(swingProgress) * (float)Math.PI);
            Vec3D rodOffset = new(-0.5D, 0.03D, 0.8D);
            rodOffset.RotateAroundX(-(angler.PrevPitch + (angler.Pitch - angler.PrevPitch) * tickDelta) * (float)Math.PI / 180.0F);
            rodOffset.RotateAroundY(-(angler.PrevYaw + (angler.Yaw - angler.PrevYaw) * tickDelta) * (float)Math.PI / 180.0F);
            rodOffset.RotateAroundY(swingOffset * 0.5F);
            rodOffset.RotateAroundX(-swingOffset * 0.7F);
            var lineStartX = angler.PrevX + (angler.X - angler.PrevX) * tickDelta + rodOffset.X;
            var lineStartY = angler.PrevY + (angler.Y - angler.PrevY) * tickDelta + rodOffset.Y;
            var lineStartZ = angler.PrevZ + (angler.Z - angler.PrevZ) * tickDelta + rodOffset.Z;
            if (Dispatcher.Options.CameraMode != CameraMode.FirstPerson)
            {
                anglerYawRadians = (angler.LastBodyYaw + (angler.BodyYaw - angler.LastBodyYaw) * tickDelta) * (float)Math.PI / 180.0F;
                sinYaw = MathHelper.Sin(anglerYawRadians);
                cosYaw = MathHelper.Cos(anglerYawRadians);
                lineStartX = angler.PrevX + (angler.X - angler.PrevX) * tickDelta - cosYaw * 0.35D - sinYaw * 0.85D;
                lineStartY = angler.PrevY + (angler.Y - angler.PrevY) * tickDelta - 0.45D;
                lineStartZ = angler.PrevZ + (angler.Z - angler.PrevZ) * tickDelta - sinYaw * 0.35D + cosYaw * 0.85D;
            }

            var bobberX = bobberEntity.PrevX + (bobberEntity.X - bobberEntity.PrevX) * tickDelta;
            var bobberY = bobberEntity.PrevY + (bobberEntity.Y - bobberEntity.PrevY) * tickDelta + 0.25D;
            var bobberZ = bobberEntity.PrevZ + (bobberEntity.Z - bobberEntity.PrevZ) * tickDelta;
            var lineDeltaX = (double)(float)(lineStartX - bobberX);
            var lineDeltaY = (double)(float)(lineStartY - bobberY);
            var lineDeltaZ = (double)(float)(lineStartZ - bobberZ);
            RenderSystem.TextureEnabled = false;
            RenderSystem.LightingEnabled = false;
            tessellator.startDrawing(3);
            tessellator.setColorOpaque_I(0x000000);
            byte segmentCount = 16;

            for (var segmentIndex = 0; segmentIndex <= segmentCount; ++segmentIndex)
            {
                var segmentProgress = segmentIndex / (float)segmentCount;
                tessellator.addVertex(x + lineDeltaX * segmentProgress, y + lineDeltaY * (segmentProgress * segmentProgress + segmentProgress) * 0.5D + 0.25D, z + lineDeltaZ * segmentProgress);
            }

            tessellator.draw(ProgramSlot.Line);
            RenderSystem.LightingEnabled = true;
            RenderSystem.TextureEnabled = true;
        }
    }

    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta) => render(target, x, y, z, yaw, tickDelta);
}
