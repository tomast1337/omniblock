using BetaSharp.Client.Options;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Entities;

public class FishingBobberEntityRenderer : EntityRenderer
{

    public void render(Entity bobberEntity, double x, double y, double z, float yaw, float tickDelta)
    {
        EntityPlayer? angler = bobberEntity.Behaviors.Find<FishingBobberBehavior>()!.Angler(bobberEntity);
        GLManager.GL.PushMatrix();
        GLManager.GL.Translate((float)x, (float)y, (float)z);
        GLManager.GL.Enable(GLEnum.RescaleNormal);
        GLManager.GL.Scale(0.5F, 0.5F, 0.5F);
        byte particleUIndex = 1;
        byte particleVIndex = 2;
        loadTexture("/particles.png");
        Tessellator tessellator = Tessellator.instance;
        float minU = (particleUIndex * 8 + 0) / 128.0F;
        float maxU = (particleUIndex * 8 + 8) / 128.0F;
        float minV = (particleVIndex * 8 + 0) / 128.0F;
        float maxV = (particleVIndex * 8 + 8) / 128.0F;
        float quadWidth = 1.0F;
        float xOffset = 0.5F;
        float yOffset = 0.5F;
        GLManager.GL.Rotate(180.0F - Dispatcher.PlayerViewY, 0.0F, 1.0F, 0.0F);
        GLManager.GL.Rotate(-Dispatcher.PlayerViewX, 1.0F, 0.0F, 0.0F);
        tessellator.startDrawingQuads();
        tessellator.setNormal(0.0F, 1.0F, 0.0F);
        tessellator.addVertexWithUV((double)(0.0F - xOffset), (double)(0.0F - yOffset), 0.0D, (double)minU, (double)maxV);
        tessellator.addVertexWithUV((double)(quadWidth - xOffset), (double)(0.0F - yOffset), 0.0D, (double)maxU, (double)maxV);
        tessellator.addVertexWithUV((double)(quadWidth - xOffset), (double)(1.0F - yOffset), 0.0D, (double)maxU, (double)minV);
        tessellator.addVertexWithUV((double)(0.0F - xOffset), (double)(1.0F - yOffset), 0.0D, (double)minU, (double)minV);
        tessellator.draw();
        GLManager.GL.Disable(GLEnum.RescaleNormal);
        GLManager.GL.PopMatrix();
        if (angler != null)
        {
            float anglerYawRadians = (angler.PrevYaw + (angler.Yaw - angler.PrevYaw) * tickDelta) * (float)Math.PI / 180.0F;
            double sinYaw = (double)MathHelper.Sin(anglerYawRadians);
            double cosYaw = (double)MathHelper.Cos(anglerYawRadians);
            float swingProgress = angler.GetSwingProgress(tickDelta);
            float swingOffset = MathHelper.Sin(MathHelper.Sqrt(swingProgress) * (float)Math.PI);
            Vec3D rodOffset = new(-0.5D, 0.03D, 0.8D);
            rodOffset.RotateAroundX(-(angler.PrevPitch + (angler.Pitch - angler.PrevPitch) * tickDelta) * (float)Math.PI / 180.0F);
            rodOffset.RotateAroundY(-(angler.PrevYaw + (angler.Yaw - angler.PrevYaw) * tickDelta) * (float)Math.PI / 180.0F);
            rodOffset.RotateAroundY(swingOffset * 0.5F);
            rodOffset.RotateAroundX(-swingOffset * 0.7F);
            double lineStartX = angler.PrevX + (angler.X - angler.PrevX) * (double)tickDelta + rodOffset.X;
            double lineStartY = angler.PrevY + (angler.Y - angler.PrevY) * (double)tickDelta + rodOffset.Y;
            double lineStartZ = angler.PrevZ + (angler.Z - angler.PrevZ) * (double)tickDelta + rodOffset.Z;
            if (Dispatcher.Options.CameraMode != CameraMode.FirstPerson)
            {
                anglerYawRadians = (angler.LastBodyYaw + (angler.BodyYaw - angler.LastBodyYaw) * tickDelta) * (float)Math.PI / 180.0F;
                sinYaw = (double)MathHelper.Sin(anglerYawRadians);
                cosYaw = (double)MathHelper.Cos(anglerYawRadians);
                lineStartX = angler.PrevX + (angler.X - angler.PrevX) * (double)tickDelta - cosYaw * 0.35D - sinYaw * 0.85D;
                lineStartY = angler.PrevY + (angler.Y - angler.PrevY) * (double)tickDelta - 0.45D;
                lineStartZ = angler.PrevZ + (angler.Z - angler.PrevZ) * (double)tickDelta - sinYaw * 0.35D + cosYaw * 0.85D;
            }

            double bobberX = bobberEntity.PrevX + (bobberEntity.X - bobberEntity.PrevX) * (double)tickDelta;
            double bobberY = bobberEntity.PrevY + (bobberEntity.Y - bobberEntity.PrevY) * (double)tickDelta + 0.25D;
            double bobberZ = bobberEntity.PrevZ + (bobberEntity.Z - bobberEntity.PrevZ) * (double)tickDelta;
            double lineDeltaX = (double)(float)(lineStartX - bobberX);
            double lineDeltaY = (double)(float)(lineStartY - bobberY);
            double lineDeltaZ = (double)(float)(lineStartZ - bobberZ);
            GLManager.GL.Disable(GLEnum.Texture2D);
            GLManager.GL.Disable(GLEnum.Lighting);
            tessellator.startDrawing(3);
            tessellator.setColorOpaque_I(0x000000);
            byte segmentCount = 16;

            for (int segmentIndex = 0; segmentIndex <= segmentCount; ++segmentIndex)
            {
                float segmentProgress = segmentIndex / (float)segmentCount;
                tessellator.addVertex(x + lineDeltaX * (double)segmentProgress, y + lineDeltaY * (double)(segmentProgress * segmentProgress + segmentProgress) * 0.5D + 0.25D, z + lineDeltaZ * (double)segmentProgress);
            }

            tessellator.draw();
            GLManager.GL.Enable(GLEnum.Lighting);
            GLManager.GL.Enable(GLEnum.Texture2D);
        }

    }

    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta)
    {
        render(target, x, y, z, yaw, tickDelta);
    }
}
