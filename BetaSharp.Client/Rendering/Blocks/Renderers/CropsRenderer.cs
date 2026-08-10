using OmniBlock.Blocks;
using OmniBlock.Textures;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Blocks.Renderers;

public class CropsRenderer : IBlockRenderer
{
    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
    {
        ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
        ctx.Tess.setColorOpaque_F(1.0F, 1.0F, 1.0F);

        int metadata = ctx.BlockReader.GetBlockMeta(pos.X, pos.Y, pos.Z);

        // Crops are pushed down slightly into the soil block
        float yOffset = pos.Y - (1.0f / 16.0f);

        RenderCropQuads(block, metadata, pos.X, yOffset, pos.Z, ref ctx);

        return true;
    }

    private void RenderCropQuads(Block block, int metadata, float x, float y, float z, ref BlockRenderContext ctx)
    {
        int textureId = block.GetTexture(0, metadata);

        if (ctx.OverrideTexture >= 0)
        {
            textureId = ctx.OverrideTexture;
        }

        ctx.Tess.setArrayLayer(Atlases.Terrain.LayerOfGridIndex(textureId));

        const float minU = 0.0F;
        const float maxU = 1.0F;
        const float minV = 0.0F;
        const float maxV = 1.0F;

        float minX = x + 0.5f - 0.25f; // Left plane X
        float maxX = x + 0.5f + 0.25f; // Right plane X
        float minZ = z + 0.5f - 0.5f; // Front plane Z
        float maxZ = z + 0.5f + 0.5f; // Back plane Z

        // --- Vertical Planes (North-South aligned) ---
        ctx.Tess.addVertexWithUV(minX, y + 1.0D, minZ, minU, minV);
        ctx.Tess.addVertexWithUV(minX, y + 0.0D, minZ, minU, maxV);
        ctx.Tess.addVertexWithUV(minX, y + 0.0D, maxZ, maxU, maxV);
        ctx.Tess.addVertexWithUV(minX, y + 1.0D, maxZ, maxU, minV);

        ctx.Tess.addVertexWithUV(minX, y + 1.0D, maxZ, minU, minV);
        ctx.Tess.addVertexWithUV(minX, y + 0.0D, maxZ, minU, maxV);
        ctx.Tess.addVertexWithUV(minX, y + 0.0D, minZ, maxU, maxV);
        ctx.Tess.addVertexWithUV(minX, y + 1.0D, minZ, maxU, minV);

        ctx.Tess.addVertexWithUV(maxX, y + 1.0D, maxZ, minU, minV);
        ctx.Tess.addVertexWithUV(maxX, y + 0.0D, maxZ, minU, maxV);
        ctx.Tess.addVertexWithUV(maxX, y + 0.0D, minZ, maxU, maxV);
        ctx.Tess.addVertexWithUV(maxX, y + 1.0D, minZ, maxU, minV);

        ctx.Tess.addVertexWithUV(maxX, y + 1.0D, minZ, minU, minV);
        ctx.Tess.addVertexWithUV(maxX, y + 0.0D, minZ, minU, maxV);
        ctx.Tess.addVertexWithUV(maxX, y + 0.0D, maxZ, maxU, maxV);
        ctx.Tess.addVertexWithUV(maxX, y + 1.0D, maxZ, maxU, minV);

        // --- Horizontal Planes (East-West aligned) ---
        // Reposition coordinates for the crossing planes
        minX = x + 0.5F - 0.5F;
        maxX = x + 0.5F + 0.5F;
        minZ = z + 0.5F - 0.25F;
        maxZ = z + 0.5F + 0.25F;

        ctx.Tess.addVertexWithUV(minX, y + 1.0D, minZ, minU, minV);
        ctx.Tess.addVertexWithUV(minX, y + 0.0D, minZ, minU, maxV);
        ctx.Tess.addVertexWithUV(maxX, y + 0.0D, minZ, maxU, maxV);
        ctx.Tess.addVertexWithUV(maxX, y + 1.0D, minZ, maxU, minV);

        ctx.Tess.addVertexWithUV(maxX, y + 1.0D, minZ, minU, minV);
        ctx.Tess.addVertexWithUV(maxX, y + 0.0D, minZ, minU, maxV);
        ctx.Tess.addVertexWithUV(minX, y + 0.0D, minZ, maxU, maxV);
        ctx.Tess.addVertexWithUV(minX, y + 1.0D, minZ, maxU, minV);

        ctx.Tess.addVertexWithUV(maxX, y + 1.0D, maxZ, minU, minV);
        ctx.Tess.addVertexWithUV(maxX, y + 0.0D, maxZ, minU, maxV);
        ctx.Tess.addVertexWithUV(minX, y + 0.0D, maxZ, maxU, maxV);
        ctx.Tess.addVertexWithUV(minX, y + 1.0D, maxZ, maxU, minV);

        ctx.Tess.addVertexWithUV(minX, y + 1.0D, maxZ, minU, minV);
        ctx.Tess.addVertexWithUV(minX, y + 0.0D, maxZ, minU, maxV);
        ctx.Tess.addVertexWithUV(maxX, y + 0.0D, maxZ, maxU, maxV);
        ctx.Tess.addVertexWithUV(maxX, y + 1.0D, maxZ, maxU, minV);
    }
}
