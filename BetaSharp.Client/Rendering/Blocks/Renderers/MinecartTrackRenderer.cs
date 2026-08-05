using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;
using BetaSharp.Textures;
using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Blocks.Renderers;

public class MinecartTrackRenderer : IBlockRenderer
{
    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
    {
        int metadata = ctx.BlockReader.GetBlockMeta(pos.X, pos.Y, pos.Z);

        int textureId = block.GetTexture(0, metadata);
        if (ctx.OverrideTexture >= 0)
        {
            textureId = ctx.OverrideTexture;
        }

        // Powered/Detector rails use bit 3 for state, but the first 8 shapes are identical
        if (RailBehavior.IsAlwaysStraight(block))
        {
            metadata &= 7;
        }

        ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
        ctx.Tess.setColorOpaque_F(1.0F, 1.0F, 1.0F);

        ctx.Tess.setArrayLayer(Atlases.Terrain.LayerOfGridIndex(textureId));

        const float minU = 0.0F;
        const float maxU = 1.0F;
        const float minV = 0.0F;
        const float maxV = 1.0F;

        float verticalOffset = 1.0F / 16.0F; // 1 pixel above the ground

        // Default vertex positions (flat square)
        float x1 = pos.X + 1, x2 = pos.X + 1, x3 = pos.X + 0, x4 = pos.X + 0;
        float z1 = pos.Z + 0, z2 = pos.Z + 1, z3 = pos.Z + 1, z4 = pos.Z + 0;

        float h1 = pos.Y + verticalOffset;
        float h2 = pos.Y + verticalOffset;
        float h3 = pos.Y + verticalOffset;
        float h4 = pos.Y + verticalOffset;

        // Handle coordinate swapping for curves and orientation
        if (metadata != 1 && metadata != 2 && metadata != 3 && metadata != 7)
        {
            if (metadata == 8)
            {
                x2 = pos.X + 0;
                x1 = x2;
                x4 = pos.X + 1;
                x3 = x4;
                z4 = pos.Z + 1;
                z1 = z4;
                z3 = pos.Z + 0;
                z2 = z3;
            }
            else if (metadata == 9)
            {
                x4 = pos.X + 0;
                x1 = x4;
                x3 = pos.X + 1;
                x2 = x3;
                z2 = pos.Z + 0;
                z1 = z2;
                z4 = pos.Z + 1;
                z3 = z4;
            }
        }
        else
        {
            x4 = pos.X + 1;
            x1 = x4;
            x3 = pos.X + 0;
            x2 = x3;
            z2 = pos.Z + 1;
            z1 = z2;
            z4 = pos.Z + 0;
            z3 = z4;
        }

        // Handle Slopes (ascending heights)
        if (metadata != 2 && metadata != 4)
        {
            if (metadata == 3 || metadata == 5)
            {
                h2++;
                h3++; // Sloping up North/South
            }
        }
        else
        {
            h1++;
            h4++; // Sloping up West/East
        }

        // Render both sides of the quad so it's visible from below (for glass/transparent floors)
        ctx.Tess.addVertexWithUV(x1, h1, z1, maxU, minV);
        ctx.Tess.addVertexWithUV(x2, h2, z2, maxU, maxV);
        ctx.Tess.addVertexWithUV(x3, h3, z3, minU, maxV);
        ctx.Tess.addVertexWithUV(x4, h4, z4, minU, minV);

        ctx.Tess.addVertexWithUV(x4, h4, z4, minU, minV);
        ctx.Tess.addVertexWithUV(x3, h3, z3, minU, maxV);
        ctx.Tess.addVertexWithUV(x2, h2, z2, maxU, maxV);
        ctx.Tess.addVertexWithUV(x1, h1, z1, maxU, minV);

        return true;
    }
}
