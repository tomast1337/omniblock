using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks.Models;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core;

namespace OmniBlock.Client.Rendering.Blocks.Renderers;

public class StairsRenderer : IBlockRenderer
{
    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
        => Draw(block, pos, ref ctx, null);

    internal bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx, CompiledStairGeometry? model)
    {
        var metadata = ctx.BlockReader is ItemRenderBlockAccess
            ? 3 : ctx.BlockReader.GetBlockMeta(pos.X, pos.Y, pos.Z);
        if (model is not null) return model.Draw(block, pos, ref ctx, metadata);
        var shape = StairShape.GetBounds(metadata);
        var baseContext = ctx with { OverrideBounds = shape.Base };
        var stepContext = ctx with { OverrideBounds = shape.Step };
        return baseContext.DrawBlock(block, pos) | stepContext.DrawBlock(block, pos);
    }
}
