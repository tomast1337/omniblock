using OmniBlock.Blocks;
using OmniBlock.Textures;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Blocks.Renderers;

public class ReedRenderer : IBlockRenderer
{
    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
    {
        ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
        var colorMultiplier = block.GetColorMultiplier(ctx.BlockReader, pos.X, pos.Y, pos.Z);
        var r = ((colorMultiplier >> 16) & 255) / 255.0F;
        var g = ((colorMultiplier >> 8) & 255) / 255.0F;
        var b = (colorMultiplier & 255) / 255.0F;

        ctx.Tess.setColorOpaque_F(r, g, b);

        float renderX = pos.X;
        float renderY = pos.Y;
        float renderZ = pos.Z;

        // Apply random organic offset for grass so it doesn't look grid-aligned
        if (block == global::OmniBlock.Registries.ContentRuntime.Current.Blocks.Get("grass")) // Assuming Block.TallGrass or equivalent
        {
            var hash = (pos.X * 3129871L) ^ (pos.Z * 116129781L) ^ pos.Y;
            hash = hash * hash * 42317861L + hash * 11L;

            renderX += (((hash >> 16) & 15L) / 15.0F - 0.5F) * 0.5F;
            renderY += (((hash >> 20) & 15L) / 15.0F - 1.0F) * 0.2F;
            renderZ += (((hash >> 24) & 15L) / 15.0F - 0.5F) * 0.5F;
        }

        RenderCrossedSquares(block, ctx.BlockReader.GetBlockMeta(pos.X, pos.Y, pos.Z), renderX, renderY, renderZ, ref ctx);
        return true;
    }

    private void RenderCrossedSquares(Block block, int metadata, float x, float y, float z,
        ref BlockRenderContext ctx)
    {
        var textureId = block.GetTexture(0, metadata);
        if (ctx.OverrideTexture >= 0)
        {
            textureId = ctx.OverrideTexture;
        }

        // Convert texture ID to UV coordinates (0.0 to 1.0 range)
        ctx.Tess.setArrayLayer(Atlases.Terrain.LayerOfGridIndex(textureId));

        const float minU = 0.0F;
        const float maxU = 1.0F;
        const float minV = 0.0F;
        const float maxV = 1.0F;

        // Magic number 0.45 means the planes stretch from 0.05 to 0.95 within the block.
        // This slight inset prevents Z-fighting (flickering) if the plant touches an adjacent solid block.
        var minOffset = 0.5F - 0.45F; // 0.05
        var maxOffset = 0.5F + 0.45F; // 0.95

        var minX = x + minOffset;
        var maxX = x + maxOffset;
        var minZ = z + minOffset;
        var maxZ = z + maxOffset;

        // --- First Diagonal Plane (Bottom-Left to Top-Right across the X/Z grid) ---

        // Front side
        ctx.Tess.addVertexWithUV(minX, y + 1.0f, minZ, minU, minV);
        ctx.Tess.addVertexWithUV(minX, y + 0.0f, minZ, minU, maxV);
        ctx.Tess.addVertexWithUV(maxX, y + 0.0f, maxZ, maxU, maxV);
        ctx.Tess.addVertexWithUV(maxX, y + 1.0f, maxZ, maxU, minV);

        // Back side (reversed winding order and UVs)
        ctx.Tess.addVertexWithUV(maxX, y + 1.0f, maxZ, minU, minV);
        ctx.Tess.addVertexWithUV(maxX, y + 0.0f, maxZ, minU, maxV);
        ctx.Tess.addVertexWithUV(minX, y + 0.0f, minZ, maxU, maxV);
        ctx.Tess.addVertexWithUV(minX, y + 1.0f, minZ, maxU, minV);

        // --- Second Diagonal Plane (Top-Left to Bottom-Right across the X/Z grid) ---

        // Front side
        ctx.Tess.addVertexWithUV(minX, y + 1.0f, maxZ, minU, minV);
        ctx.Tess.addVertexWithUV(minX, y + 0.0f, maxZ, minU, maxV);
        ctx.Tess.addVertexWithUV(maxX, y + 0.0f, minZ, maxU, maxV);
        ctx.Tess.addVertexWithUV(maxX, y + 1.0f, minZ, maxU, minV);

        // Back side (reversed winding order and UVs)
        ctx.Tess.addVertexWithUV(maxX, y + 1.0f, minZ, minU, minV);
        ctx.Tess.addVertexWithUV(maxX, y + 0.0f, minZ, minU, maxV);
        ctx.Tess.addVertexWithUV(minX, y + 0.0f, maxZ, maxU, maxV);
        ctx.Tess.addVertexWithUV(minX, y + 1.0f, maxZ, maxU, minV);
    }
}
