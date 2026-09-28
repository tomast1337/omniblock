using OmniBlock.Blocks;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Blocks.Renderers;

public class FenceRenderer : IBlockRenderer
{
    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
    {
        var mask = (ctx.BlockReader.GetBlockId(pos.X - 1, pos.Y, pos.Z) == block.Id ? FenceShape.West : 0) |
                   (ctx.BlockReader.GetBlockId(pos.X + 1, pos.Y, pos.Z) == block.Id ? FenceShape.East : 0) |
                   (ctx.BlockReader.GetBlockId(pos.X, pos.Y, pos.Z - 1) == block.Id ? FenceShape.North : 0) |
                   (ctx.BlockReader.GetBlockId(pos.X, pos.Y, pos.Z + 1) == block.Id ? FenceShape.South : 0);
        foreach (var box in FenceShape.GetBounds(mask))
        {
            var part = ctx with { OverrideBounds = box };
            part.DrawBlock(block, pos);
        }
        return true;
    }
}
