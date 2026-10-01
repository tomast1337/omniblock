using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks.Models;
using OmniBlock.Textures;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Blocks.Renderers;

public class ReedRenderer : IBlockRenderer
{
    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
        => Draw(block, pos, ref ctx, null);

    internal bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx,
        CompiledCrossedPlantGeometry? geometry)
    {
        ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
        var colorMultiplier = block.GetColorMultiplier(ctx.BlockReader, pos.X, pos.Y, pos.Z);
        var r = ((colorMultiplier >> 16) & 255) / 255.0F;
        var g = ((colorMultiplier >> 8) & 255) / 255.0F;
        var b = (colorMultiplier & 255) / 255.0F;

        ctx.Tess.setColorOpaque_F(r, g, b);

        var offset = CrossedPlantGeometry.Offset(pos.X, pos.Y, pos.Z,
            block == ctx.Blocks.Get("grass"));
        RenderCrossedSquares(block, ctx.BlockReader.GetBlockMeta(pos.X, pos.Y, pos.Z),
            pos.X + offset.X, pos.Y + offset.Y, pos.Z + offset.Z, ref ctx, geometry);
        return true;
    }

    private void RenderCrossedSquares(Block block, int metadata, float x, float y, float z,
        ref BlockRenderContext ctx, CompiledCrossedPlantGeometry? geometry)
    {
        var textureId = block.GetTexture(0, metadata);
        if (ctx.OverrideTexture >= 0)
        {
            textureId = ctx.OverrideTexture;
        }

        var tess = ctx.Tess;
        tess.setArrayLayer(Atlases.Terrain.LayerOfGridIndex(textureId));
        foreach (ref readonly var quad in (geometry ?? CrossedPlantGeometry.Builtin).Quads)
        {
            Emit(quad.A);
            Emit(quad.B);
            Emit(quad.C);
            Emit(quad.D);
        }

        void Emit(CrossedPlantGeometry.Vertex vertex) =>
            tess.addVertexWithUV(x + vertex.X, y + vertex.Y, z + vertex.Z,
                vertex.U, vertex.V);
    }
}
