using OmniBlock.Blocks;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Blocks.Renderers;

public class FenceRenderer : IBlockRenderer
{
    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
    {
        var hasRendered = true;

        // 1. Render the central vertical post
        var postMin = 6.0F / 16.0F;
        var postMax = 10.0F / 16.0F;

        // Clone the context and apply the new bounding box for the post
        var postCtx = ctx with
        {
            OverrideBounds = new Box(postMin, 0.0F, postMin, postMax, 1.0F, postMax)
        };
        postCtx.DrawBlock(block, pos);

        // Check for adjacent fences using 'ctx.World' and 'pos'
        var connectsWest = ctx.BlockReader.GetBlockId(pos.X - 1, pos.Y, pos.Z) == block.Id;
        var connectsEast = ctx.BlockReader.GetBlockId(pos.X + 1, pos.Y, pos.Z) == block.Id;
        var connectsNorth = ctx.BlockReader.GetBlockId(pos.X, pos.Y, pos.Z - 1) == block.Id;
        var connectsSouth = ctx.BlockReader.GetBlockId(pos.X, pos.Y, pos.Z + 1) == block.Id;

        var connectsX = connectsWest || connectsEast;
        var connectsZ = connectsNorth || connectsSouth;

        // If the fence is completely isolated, default to drawing small stubs along the X-axis
        if (!connectsX && !connectsZ)
        {
            connectsX = true;
        }

        // Base depth/thickness for the horizontal connecting bars
        var barDepthMin = 7.0F / 16.0F;
        var barDepthMax = 9.0F / 16.0F;

        // Determine how far the bars extend based on neighbor connections
        var barMinX = connectsWest ? 0.0F : barDepthMin;
        var barMaxX = connectsEast ? 1.0F : barDepthMax;
        var barMinZ = connectsNorth ? 0.0F : barDepthMin;
        var barMaxZ = connectsSouth ? 1.0F : barDepthMax;

        // 2. Render Top Connecting Bars
        var topBarMinY = 12.0F / 16.0F;
        var topBarMaxY = 15.0F / 16.0F;

        if (connectsX)
        {
            var topXCtx = ctx with
            {
                OverrideBounds = new Box(barMinX, topBarMinY, barDepthMin, barMaxX, topBarMaxY, barDepthMax)
            };
            topXCtx.DrawBlock(block, pos);
        }

        if (connectsZ)
        {
            var topZCtx = ctx with
            {
                OverrideBounds = new Box(barDepthMin, topBarMinY, barMinZ, barDepthMax, topBarMaxY, barMaxZ)
            };
            topZCtx.DrawBlock(block, pos);
        }

        // 3. Render Bottom Connecting Bars
        var bottomBarMinY = 6.0F / 16.0F;
        var bottomBarMaxY = 9.0F / 16.0F;

        if (connectsX)
        {
            var bottomXCtx = ctx with
            {
                OverrideBounds = new Box(barMinX, bottomBarMinY, barDepthMin, barMaxX, bottomBarMaxY, barDepthMax)
            };
            bottomXCtx.DrawBlock(block, pos);
        }

        if (connectsZ)
        {
            var bottomZCtx = ctx with
            {
                OverrideBounds = new Box(barDepthMin, bottomBarMinY, barMinZ, barDepthMax, bottomBarMaxY, barMaxZ)
            };
            bottomZCtx.DrawBlock(block, pos);
        }

        return hasRendered;
    }
}
