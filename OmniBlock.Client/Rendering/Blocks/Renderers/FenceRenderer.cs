using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks.Models;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Blocks.Renderers;

public class FenceRenderer : IBlockRenderer
{
    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx) => Draw(block, pos, ref ctx, null);

    internal bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx, CompiledFenceGeometry? model)
    {
        if (model is not null && ctx.OverrideTexture < 0) return model.Draw(block, pos, ref ctx);
        var mask = FenceShape.ConnectionMask(ctx.BlockReader, block.Id, pos.X, pos.Y, pos.Z);
        foreach (var box in FenceShape.GetBounds(mask))
        {
            var part = ctx with { OverrideBounds = box };
            part.DrawBlock(block, pos);
        }
        return true;
    }
}
