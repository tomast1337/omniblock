using BetaSharp.Blocks;
using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Blocks.Renderers;

public class LadderRenderer : IBlockRenderer
{
    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
    {
        int textureId = block.GetTexture(0);
        if (ctx.OverrideTexture >= 0)
        {
            textureId = ctx.OverrideTexture;
        }

        float luminance = block.GetLuminance(ctx.Lighting, pos.X, pos.Y, pos.Z);
        ctx.Tess.setColorOpaque_F(luminance, luminance, luminance);

        int texU = (textureId & 15) << 4;
        int texV = textureId & 240;
        float minU = texU / 256.0f;
        float maxU = (texU + 15.99f) / 256.0f;
        float minV = texV / 256.0f;
        float maxV = (texV + 15.99f) / 256.0f;

        int metadata = ctx.BlockReader.GetBlockMeta(pos.X, pos.Y, pos.Z);

        // Push the ladder slightly off the wall
        float offset = 0.05f;

        if (metadata == 5)
        {
            ctx.Tess.addVertexWithUV(pos.X + offset, pos.Y + 1.0D, pos.Z + 1.0D, minU, minV);
            ctx.Tess.addVertexWithUV(pos.X + offset, pos.Y + 0.0D, pos.Z + 1.0D, minU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + offset, pos.Y + 0.0D, pos.Z + 0.0D, maxU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + offset, pos.Y + 1.0D, pos.Z + 0.0D, maxU, minV);
        }
        else if (metadata == 4)
        {
            ctx.Tess.addVertexWithUV(pos.X + 1.0D - offset, pos.Y + 0.0D, pos.Z + 1.0D, maxU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 1.0D - offset, pos.Y + 1.0D, pos.Z + 1.0D, maxU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 1.0D - offset, pos.Y + 1.0D, pos.Z + 0.0D, minU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 1.0D - offset, pos.Y + 0.0D, pos.Z + 0.0D, minU, maxV);
        }
        else if (metadata == 3)
        {
            ctx.Tess.addVertexWithUV(pos.X + 1.0D, pos.Y + 0.0D, pos.Z + offset, maxU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 1.0D, pos.Y + 1.0D, pos.Z + offset, maxU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 0.0D, pos.Y + 1.0D, pos.Z + offset, minU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 0.0D, pos.Y + 0.0D, pos.Z + offset, minU, maxV);
        }
        else if (metadata == 2)
        {
            ctx.Tess.addVertexWithUV(pos.X + 1.0D, pos.Y + 1.0D, pos.Z + 1.0D - offset, minU, minV);
            ctx.Tess.addVertexWithUV(pos.X + 1.0D, pos.Y + 0.0D, pos.Z + 1.0D - offset, minU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 0.0D, pos.Y + 0.0D, pos.Z + 1.0D - offset, maxU, maxV);
            ctx.Tess.addVertexWithUV(pos.X + 0.0D, pos.Y + 1.0D, pos.Z + 1.0D - offset, maxU, minV);
        }

        return true;
    }
}
