using OmniBlock.Blocks;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Blocks.Renderers;

public class CactusRenderer : IBlockRenderer
{
    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
    {
        // The whole cube, not the block's bounding box. That box is inset by a pixel on X and Z
        // because that is where a cactus hurts you, and rendering through it drew the sides a pixel
        // thin and left the full-width top and bottom overhanging them. A cactus is inset by moving
        // each side face inward below, and by nothing else.
        var bounds = ctx.OverrideBounds ?? new Box(0.0, 0.0, 0.0, 1.0, 1.0, 1.0);
        var hasRendered = false;

        // Force the helper to use flat shading so it doesn't override our colors with the dummy struct
        var flatCtx = ctx with
        {
            EnableAo = false,
            OverrideBounds = bounds
        };

        // 1. Calculate the specific biome/tint color for this cactus
        var colorMultiplier = block.GetColorMultiplier(ctx.BlockReader, pos.X, pos.Y, pos.Z);
        var red = ((colorMultiplier >> 16) & 255) / 255.0F;
        var green = ((colorMultiplier >> 8) & 255) / 255.0F;
        var blue = (colorMultiplier & 255) / 255.0F;

        // 2. Base directional lighting multipliers
        const float lightBottom = 0.5F;
        const float lightTop = 1.0F;
        const float lightZ = 0.8F; // East/West faces
        const float lightX = 0.6F; // North/South faces

        // Pre-calculate tinted colors for each face
        float rBottom = lightBottom * red, gBottom = lightBottom * green, bBottom = lightBottom * blue;
        float rTop = lightTop * red, gTop = lightTop * green, bTop = lightTop * blue;
        float rZ = lightZ * red, gZ = lightZ * green, bZ = lightZ * blue;
        float rX = lightX * red, gX = lightX * green, bX = lightX * blue;

        // 1/16th of a block = exactly 1 pixel width in a standard 16x16 texture
        var inset = 1.0F / 16.0F;


        FaceColors dummyColors = new();

        // --- Bottom Face (Y - 1) ---
        if (flatCtx.RenderAllFaces || bounds.MinY > 0.0D || block.IsSideVisible(ctx.BlockReader, pos.X, pos.Y - 1, pos.Z, 0))
        {
            ctx.SetLightAt(block, pos.X, pos.Y - 1, pos.Z);
            ctx.Tess.setColorOpaque_F(rBottom, gBottom, bBottom);

            var tex = block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, 0);
            flatCtx.DrawBottomFace(block, new Vec3D(pos.X, pos.Y, pos.Z), dummyColors, tex);
            hasRendered = true;
        }

        // --- Top Face (Y + 1) ---
        if (flatCtx.RenderAllFaces || bounds.MaxY < 1.0D || block.IsSideVisible(ctx.BlockReader, pos.X, pos.Y + 1, pos.Z, Side.Up))
        {
            if (Math.Abs(bounds.MaxY - 1.0D) > 0.1 && !block.Material.IsFluid)
            {
                ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
            }
            else
            {
                ctx.SetLightAt(block, pos.X, pos.Y + 1, pos.Z);
            }

            ctx.Tess.setColorOpaque_F(rTop, gTop, bTop);

            var tex = block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.Up);
            flatCtx.DrawTopFace(block, new Vec3D(pos.X, pos.Y, pos.Z), dummyColors, tex);
            hasRendered = true;
        }

        // --- East Face (Z - 1) ---
        if (flatCtx.RenderAllFaces || bounds.MinZ > 0.0D || block.IsSideVisible(ctx.BlockReader, pos.X, pos.Y, pos.Z - 1, Side.North))
        {
            if (bounds.MinZ > 0.0D)
            {
                ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
            }
            else
            {
                ctx.SetLightAt(block, pos.X, pos.Y, pos.Z - 1);
            }

            ctx.Tess.setColorOpaque_F(rZ, gZ, bZ);

            ctx.Tess.setTranslationF(0.0F, 0.0F, inset);

            var tex = block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.North);
            flatCtx.DrawEastFace(block, new Vec3D(pos.X, pos.Y, pos.Z), dummyColors, tex);

            ctx.Tess.setTranslationF(0.0F, 0.0F, -inset);
            hasRendered = true;
        }

        // --- West Face (Z + 1) ---
        if (flatCtx.RenderAllFaces || bounds.MaxZ < 1.0D || block.IsSideVisible(ctx.BlockReader, pos.X, pos.Y, pos.Z + 1, Side.South))
        {
            if (bounds.MaxZ < 1.0D)
            {
                ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
            }
            else
            {
                ctx.SetLightAt(block, pos.X, pos.Y, pos.Z + 1);
            }

            ctx.Tess.setColorOpaque_F(rZ, gZ, bZ);

            ctx.Tess.setTranslationF(0.0F, 0.0F, -inset);

            var tex = block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.South);
            flatCtx.DrawWestFace(block, new Vec3D(pos.X, pos.Y, pos.Z), dummyColors, tex);

            ctx.Tess.setTranslationF(0.0F, 0.0F, inset);
            hasRendered = true;
        }

        // --- North Face (X - 1) ---
        if (flatCtx.RenderAllFaces || bounds.MinX > 0.0D || block.IsSideVisible(ctx.BlockReader, pos.X - 1, pos.Y, pos.Z, Side.West))
        {
            if (bounds.MinX > 0.0D)
            {
                ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
            }
            else
            {
                ctx.SetLightAt(block, pos.X - 1, pos.Y, pos.Z);
            }

            ctx.Tess.setColorOpaque_F(rX, gX, bX);

            ctx.Tess.setTranslationF(inset, 0.0F, 0.0F);

            var tex = block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.West);
            flatCtx.DrawNorthFace(block, new Vec3D(pos.X, pos.Y, pos.Z), dummyColors, tex);

            ctx.Tess.setTranslationF(-inset, 0.0F, 0.0F);
            hasRendered = true;
        }

        // --- South Face (X + 1) ---
        if (flatCtx.RenderAllFaces || bounds.MaxX < 1.0D || block.IsSideVisible(ctx.BlockReader, pos.X + 1, pos.Y, pos.Z, Side.East))
        {
            if (bounds.MaxX < 1.0D)
            {
                ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
            }
            else
            {
                ctx.SetLightAt(block, pos.X + 1, pos.Y, pos.Z);
            }

            ctx.Tess.setColorOpaque_F(rX, gX, bX);

            ctx.Tess.setTranslationF(-inset, 0.0F, 0.0F);

            var tex = block.GetTextureId(ctx.BlockReader, pos.X, pos.Y, pos.Z, Side.East);
            flatCtx.DrawSouthFace(block, new Vec3D(pos.X, pos.Y, pos.Z), dummyColors, tex);

            ctx.Tess.setTranslationF(inset, 0.0F, 0.0F);
            hasRendered = true;
        }

        return hasRendered;
    }
}
