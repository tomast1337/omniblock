using OmniBlock.Client.Rendering.Core;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Util.Maths;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Entities;

public class PaintingEntityRenderer : EntityRenderer
{
    public override void Render(Entity target, double x, double y, double z, float yaw, float tickDelta) => RenderPainting(target, x, y, z, yaw);

    private void RenderPainting(Entity paintingEntity, double x, double y, double z, float yaw)
    {
        RenderSystem.ModelView.Push();
        RenderSystem.ModelView.Translate((float)x, (float)y, (float)z);
        RenderSystem.ModelView.Rotate(yaw, 0.0F, 1.0F, 0.0F);

        loadTexture("/art/kz.png");

        var art = paintingEntity.Behaviors.Find<HangingArtBehavior>()!.Art(paintingEntity)!;
        var pixelScale = 1.0F / 16.0F;
        RenderSystem.ModelView.Scale(pixelScale, pixelScale, pixelScale);

        RenderPaintingQuads(paintingEntity, art.SizeX, art.SizeY, art.OffsetX, art.OffsetY);

        RenderSystem.ModelView.Pop();
    }

    private void RenderPaintingQuads(Entity paintingEntity, int width, int height, int textureX, int textureY)
    {
        var leftBound = -width / 2.0F;
        var bottomBound = -height / 2.0F;
        var frontZ = -0.5F;
        var backZ = 0.5F;

        for (var tileX = 0; tileX < width / 16; ++tileX)
        {
            for (var tileY = 0; tileY < height / 16; ++tileY)
            {
                var xMax = leftBound + (tileX + 1) * 16;
                var xMin = leftBound + tileX * 16;
                var yMax = bottomBound + (tileY + 1) * 16;
                var yMin = bottomBound + tileY * 16;

                UpdateLighting(paintingEntity, (xMax + xMin) / 2.0F, (yMax + yMin) / 2.0F);

                var uMax = (textureX + width - tileX * 16) / 256.0F;
                var uMin = (textureX + width - (tileX + 1) * 16) / 256.0F;
                var vMax = (textureY + height - tileY * 16) / 256.0F;
                var vMin = (textureY + height - (tileY + 1) * 16) / 256.0F;

                var edgeUMin = 12.0F / 16.0F;
                var edgeUMax = 13.0F / 16.0F;
                var edgeVMin = 0.0F;
                var edgeVMax = 1.0F / 16.0F;

                var tess = Tessellator.instance;
                tess.startDrawingQuads();

                // Front Face (The Art)
                tess.setNormal(0.0F, 0.0F, -1.0F);
                tess.addVertexWithUV(xMax, yMin, frontZ, uMin, vMax);
                tess.addVertexWithUV(xMin, yMin, frontZ, uMax, vMax);
                tess.addVertexWithUV(xMin, yMax, frontZ, uMax, vMin);
                tess.addVertexWithUV(xMax, yMax, frontZ, uMin, vMin);

                // Back Face (The Wood)
                tess.setNormal(0.0F, 0.0F, 1.0F);
                tess.addVertexWithUV(xMax, yMax, backZ, edgeUMin, edgeVMin);
                tess.addVertexWithUV(xMin, yMax, backZ, edgeUMax, edgeVMin);
                tess.addVertexWithUV(xMin, yMin, backZ, edgeUMax, edgeVMax);
                tess.addVertexWithUV(xMax, yMin, backZ, edgeUMin, edgeVMax);

                // Side/Top/Bottom Edges
                tess.setNormal(0.0F, -1.0F, 0.0F); // Top
                tess.addVertexWithUV(xMax, yMax, frontZ, edgeUMin, edgeVMin);
                tess.addVertexWithUV(xMin, yMax, frontZ, edgeUMax, edgeVMin);
                tess.addVertexWithUV(xMin, yMax, backZ, edgeUMax, edgeVMax);
                tess.addVertexWithUV(xMax, yMax, backZ, edgeUMin, edgeVMax);

                tess.setNormal(0.0F, 1.0F, 0.0F); // Bottom
                tess.addVertexWithUV(xMax, yMin, backZ, edgeUMin, edgeVMin);
                tess.addVertexWithUV(xMin, yMin, backZ, edgeUMax, edgeVMin);
                tess.addVertexWithUV(xMin, yMin, frontZ, edgeUMax, edgeVMax);
                tess.addVertexWithUV(xMax, yMin, frontZ, edgeUMin, edgeVMax);

                tess.setNormal(-1.0F, 0.0F, 0.0F); // Left
                tess.addVertexWithUV(xMax, yMax, backZ, edgeUMax, edgeVMin);
                tess.addVertexWithUV(xMax, yMin, backZ, edgeUMax, edgeVMax);
                tess.addVertexWithUV(xMax, yMin, frontZ, edgeUMin, edgeVMax);
                tess.addVertexWithUV(xMax, yMax, frontZ, edgeUMin, edgeVMin);

                tess.setNormal(1.0F, 0.0F, 0.0F); // Right
                tess.addVertexWithUV(xMin, yMax, frontZ, edgeUMax, edgeVMin);
                tess.addVertexWithUV(xMin, yMin, frontZ, edgeUMax, edgeVMax);
                tess.addVertexWithUV(xMin, yMin, backZ, edgeUMin, edgeVMax);
                tess.addVertexWithUV(xMin, yMax, backZ, edgeUMin, edgeVMin);

                tess.draw(ProgramSlot.Entities);
            }
        }
    }

    private void UpdateLighting(Entity paintingEntity, float offsetX, float offsetY)
    {
        var checkX = MathHelper.Floor(paintingEntity.X);
        var checkY = MathHelper.Floor(paintingEntity.Y + offsetY / 16.0F);
        var checkZ = MathHelper.Floor(paintingEntity.Z);

        // Offset the light check based on orientation to ensure we aren't sampling inside the wall
        switch (paintingEntity.Behaviors.Find<HangingArtBehavior>()!.Direction(paintingEntity))
        {
            case 0: checkX = MathHelper.Floor(paintingEntity.X + offsetX / 16.0F); break;
            case 1: checkZ = MathHelper.Floor(paintingEntity.Z - offsetX / 16.0F); break;
            case 2: checkX = MathHelper.Floor(paintingEntity.X - offsetX / 16.0F); break;
            case 3: checkZ = MathHelper.Floor(paintingEntity.Z + offsetX / 16.0F); break;
        }

        var light = Dispatcher.World.GetLuminance(checkX, checkY, checkZ);
        RenderSystem.Color = new Vector4D<float>(light, light, light, 1.0F);
    }
}
