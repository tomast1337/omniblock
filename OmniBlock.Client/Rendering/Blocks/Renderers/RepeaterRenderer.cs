using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Blocks.Renderers;

public class RepeaterRenderer : IBlockRenderer
{
    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
    {
        var metadata = ctx.BlockReader.GetBlockMeta(pos.X, pos.Y, pos.Z);
        var direction = metadata & 3;
        var delay = (metadata & 12) >> 2;
        // 1. Base Rendering
        var slabCtx = ctx with
        {
            EnableAo = true,
            AoBlendMode = 0,
            UvRotateTop = direction % 4
        };

        slabCtx.DrawBlock(block, pos);

        // 2. Prepare Torch Rendering
        var luminance = 1.0F;
        ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
        if (ctx.Blocks.GetLightEmission(block.Id) > 0)
        {
            // Halfway to full bright is not a light level, so the lit repeater's torch simply is
            // full bright now. It emits, so it was already close.
            ctx.SetFullBright();
        }

        ctx.Tess.setColorOpaque_F(luminance, luminance, luminance);

        // Torch pins are rendered slightly below the slab surface so they sit inside it
        var torchVerticalOffset = -0.1875F;
        var staticTorchX = 0.0F;
        var staticTorchZ = 0.0F;
        var delayTorchX = 0.0F;
        var delayTorchZ = 0.0F;

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
