using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;
using BetaSharp.Textures;
using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Blocks.Renderers;

public class BedRenderer : IBlockRenderer
{
    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
    {
        Box bounds = ctx.OverrideBounds ?? block.BoundingBox;
        int metadata = ctx.BlockReader.GetBlockMeta(pos.X, pos.Y, pos.Z);
        int direction = BedBehavior.GetDirection(metadata);
        bool isHead = BedBehavior.IsHeadOfBed(metadata);

        const float lightBottom = 0.5F;
        const float lightTop = 1.0F;
        const float lightZ = 0.8F;
        const float lightX = 0.6F;

        // BOTTOM FACE
        ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
        ctx.Tess.setColorOpaque_F(lightBottom, lightBottom, lightBottom);

        int texBottom = block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, 0);
        ctx.Tess.setArrayLayer(Atlases.Terrain.LayerOfGridIndex(texBottom));

        const float minU = 0.0F;
        const float maxU = 1.0F;
        const float minV = 0.0F;
        const float maxV = 1.0F;

        float minX = (float)(pos.X + bounds.MinX);
        float maxX = (float)(pos.X + bounds.MaxX);
        float bedBottomY = (float)(pos.Y + bounds.MinY + 0.1875f); // Bed legs are 3 pixels tall (3/16 = 0.1875)
        float minZ = (float)(pos.Z + bounds.MinZ);
        float maxZ = (float)(pos.Z + bounds.MaxZ);

        ctx.Tess.addVertexWithUV(minX, bedBottomY, maxZ, minU, maxV);
        ctx.Tess.addVertexWithUV(minX, bedBottomY, minZ, minU, minV);
        ctx.Tess.addVertexWithUV(maxX, bedBottomY, minZ, maxU, minV);
        ctx.Tess.addVertexWithUV(maxX, bedBottomY, maxZ, maxU, maxV);

        // TOP FACE
        ctx.SetLightAt(block, pos.X, pos.Y + 1, pos.Z);
        ctx.Tess.setColorOpaque_F(lightTop, lightTop, lightTop);

        int texTop = block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.Up);
        ctx.Tess.setArrayLayer(Atlases.Terrain.LayerOfGridIndex(texTop));

        float u1 = minU, u2 = maxU, u3 = minU, u4 = maxU;
        float v1 = minV, v2 = minV, v3 = maxV, v4 = maxV;

        // Rotate top texture based on bed orientation
        if (direction == 0) // South
        {
            u2 = minU;
            v2 = maxV;
            u3 = maxU;
            v3 = minV;
        }
        else if (direction == 2) // North
        {
            u1 = maxU;
            v1 = maxV;
            u4 = minU;
            v4 = minV;
        }
        else if (direction == 3) // East
        {
            u1 = maxU;
            v1 = maxV;
            u4 = minU;
            v4 = minV;
            u2 = minU;
            v2 = maxV;
            u3 = maxU;
            v3 = minV;
        }

        float bedTopY = (float)(pos.Y + bounds.MaxY);

        ctx.Tess.addVertexWithUV(maxX, bedTopY, maxZ, u3, v3);
        ctx.Tess.addVertexWithUV(maxX, bedTopY, minZ, u1, v1);
        ctx.Tess.addVertexWithUV(minX, bedTopY, minZ, u2, v2);
        ctx.Tess.addVertexWithUV(minX, bedTopY, maxZ, u4, v4);

        // SIDE FACES
        int forwardDir = Facings.ToDir[direction];
        if (isHead)
        {
            forwardDir = Facings.ToDir[Facings.Opposite[direction]];
        }

        byte textureFlipDir = 4;
        switch (direction)
        {
            case 0: textureFlipDir = 5; break;
            case 1:
                textureFlipDir = 3;
                goto case 2;
            case 2:
            default: break;
            case 3: textureFlipDir = 2; break;
        }

        var flatCtx = ctx with { EnableAo = false };
        // East Face (Z - 1)
        if (forwardDir != 2 && (ctx.RenderAllFaces || block.IsSideVisible(ctx.BlockReader, pos.X, pos.Y, pos.Z - 1, Side.North)))
        {
            if (bounds.MinZ > 0.0f) { ctx.SetLightAt(block, pos.X, pos.Y, pos.Z); }
            else { ctx.SetLightAt(block, pos.X, pos.Y, pos.Z - 1); }
            ctx.Tess.setColorOpaque_F(lightZ, lightZ, lightZ);

            flatCtx.FlipTexture = textureFlipDir == 2;
            flatCtx.DrawEastFace(block, new Vec3D(pos.X, pos.Y, pos.Z), new FaceColors(),
                block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.North));
        }

        // West Face (Z + 1)
        if (forwardDir != 3 && (ctx.RenderAllFaces || block.IsSideVisible(ctx.BlockReader, pos.X, pos.Y, pos.Z + 1, Side.South)))
        {
            if (bounds.MaxZ < 1.0f) { ctx.SetLightAt(block, pos.X, pos.Y, pos.Z); }
            else { ctx.SetLightAt(block, pos.X, pos.Y, pos.Z + 1); }
            ctx.Tess.setColorOpaque_F(lightZ, lightZ, lightZ);

            flatCtx.FlipTexture = textureFlipDir == 3;
            flatCtx.DrawWestFace(block, new Vec3D(pos.X, pos.Y, pos.Z), new FaceColors(),
                block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.South));
        }

        // North Face (X - 1)
        if (forwardDir != 4 && (ctx.RenderAllFaces || block.IsSideVisible(ctx.BlockReader, pos.X - 1, pos.Y, pos.Z, Side.West)))
        {
            if (bounds.MinX > 0.0f) { ctx.SetLightAt(block, pos.X, pos.Y, pos.Z); }
            else { ctx.SetLightAt(block, pos.X - 1, pos.Y, pos.Z); }
            ctx.Tess.setColorOpaque_F(lightX, lightX, lightX);

            flatCtx.FlipTexture = textureFlipDir == 4;
            flatCtx.DrawNorthFace(block, new Vec3D(pos.X, pos.Y, pos.Z), new FaceColors(),
                block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.West));
        }

        // South Face (X + 1)
        if (forwardDir != 5 && (ctx.RenderAllFaces || block.IsSideVisible(ctx.BlockReader, pos.X + 1, pos.Y, pos.Z, Side.East)))
        {
            if (bounds.MaxX < 1.0f) { ctx.SetLightAt(block, pos.X, pos.Y, pos.Z); }
            else { ctx.SetLightAt(block, pos.X + 1, pos.Y, pos.Z); }
            ctx.Tess.setColorOpaque_F(lightX, lightX, lightX);

            flatCtx.FlipTexture = textureFlipDir == 5;
            flatCtx.DrawSouthFace(block, new Vec3D(pos.X, pos.Y, pos.Z), new FaceColors(),
                block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.East));
        }

        return true;
    }
}
