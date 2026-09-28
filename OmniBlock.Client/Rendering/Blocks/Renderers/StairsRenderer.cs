using OmniBlock.Blocks;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core;

namespace OmniBlock.Client.Rendering.Blocks.Renderers;

public class StairsRenderer : IBlockRenderer
{
    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
    {
        var metadata = ctx.BlockReader is ItemRenderBlockAccess
            ? 3 : ctx.BlockReader.GetBlockMeta(pos.X, pos.Y, pos.Z);
        var shape = StairShape.GetBounds(metadata);
        var baseContext = ctx with { OverrideBounds = shape.Base };
        var stepContext = ctx with { OverrideBounds = shape.Step };
        return baseContext.DrawBlock(block, pos) | stepContext.DrawBlock(block, pos);
    }
}
