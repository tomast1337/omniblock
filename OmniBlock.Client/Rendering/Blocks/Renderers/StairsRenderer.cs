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
        StairShape.Resolved shape;
        if (ctx.BlockReader is ItemRenderBlockAccess)
        {
            var straight = StairShape.GetBounds(metadata);
            shape = new StairShape.Resolved(straight.Base, straight.Step, null);
        }
        else shape = StairShape.Resolve(ctx.BlockReader, ctx.Blocks, pos.X, pos.Y, pos.Z, metadata);
        if (model is not null) return model.Draw(block, pos, ref ctx, metadata, shape);
        var baseContext = ctx with { OverrideBounds = shape.Base };
        var stepContext = ctx with { OverrideBounds = shape.Step };
        var rendered = baseContext.DrawBlock(block, pos) | stepContext.DrawBlock(block, pos);
        if (shape.Extra is { } extra)
        {
            var extraContext = ctx with { OverrideBounds = extra };
            rendered |= extraContext.DrawBlock(block, pos);
        }
        return rendered;
    }
}
