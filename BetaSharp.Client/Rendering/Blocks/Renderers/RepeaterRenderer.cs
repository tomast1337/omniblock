using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;
using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Blocks.Renderers;

public class RepeaterRenderer : IBlockRenderer
{
    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
    {
        int metadata = ctx.BlockReader.GetBlockMeta(pos.X, pos.Y, pos.Z);
        int direction = metadata & 3;
        int delay = (metadata & 12) >> 2;
        // 1. Base Rendering
        var slabCtx = ctx with { EnableAo = true, AoBlendMode = 0, UvRotateTop = direction % 4 };

        slabCtx.DrawBlock(block, pos);

        // 2. Prepare Torch Rendering
        float luminance = 1.0F;
        ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
        if (Block.BlocksLightLuminance[block.Id] > 0)
        {
            // Halfway to full bright is not a light level, so the lit repeater's torch simply is
            // full bright now. It emits, so it was already close.
            ctx.SetFullBright();
        }

        ctx.Tess.setColorOpaque_F(luminance, luminance, luminance);

        // Torch pins are rendered slightly below the slab surface so they sit inside it
        float torchVerticalOffset = -0.1875F;
        float staticTorchX = 0.0F;
        float staticTorchZ = 0.0F;
        float delayTorchX = 0.0F;
        float delayTorchZ = 0.0F;

        switch (direction)
        {
            case 0: // South
                delayTorchZ = -0.3125f;
                staticTorchZ = RepeaterBehavior.RenderOffset[delay];
                break;
            case 1: // West
                delayTorchX = 0.3125f;
                staticTorchX = -RepeaterBehavior.RenderOffset[delay];
                break;
            case 2: // North
                delayTorchZ = 0.3125f;
                staticTorchZ = -RepeaterBehavior.RenderOffset[delay];
                break;
            case 3: // East
                delayTorchX = -0.3125f;
                staticTorchX = RepeaterBehavior.RenderOffset[delay];
                break;
        }

        // 3. Render the two torch pins
        slabCtx.DrawTorch(block, new Vec3D(pos.X + staticTorchX, pos.Y + torchVerticalOffset, pos.Z + staticTorchZ), 0.0f, 0.0f);
        slabCtx.DrawTorch(block, new Vec3D(pos.X + delayTorchX, pos.Y + torchVerticalOffset, pos.Z + delayTorchZ), 0.0f, 0.0f);
        return true;
    }
}
