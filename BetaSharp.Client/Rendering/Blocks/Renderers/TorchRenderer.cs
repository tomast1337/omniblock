using BetaSharp.Blocks;
using BetaSharp.Util.Maths;

namespace BetaSharp.Client.Rendering.Blocks.Renderers;

public class TorchRenderer : IBlockRenderer
{
    public bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx)
    {
        int metadata = ctx.BlockReader.GetBlockMeta(pos.X, pos.Y, pos.Z);

        if (Block.BlocksLightLuminance[block.Id] > 0)
        {
            ctx.SetFullBright();
        }
        else
        {
            ctx.SetLightAt(block, pos.X, pos.Y, pos.Z);
        }

        ctx.Tess.setColorOpaque_F(1.0F, 1.0F, 1.0F);

        float tiltAmount = 0.4f;
        float horizontalOffset = 0.5f - tiltAmount;
        float verticalOffset = 0.2f;

        if (metadata == 1) // Attached to West wall (pointing East)
        {
            ctx.DrawTorch(block, new Vec3D(pos.X - horizontalOffset, pos.Y + verticalOffset, pos.Z), -tiltAmount, 0.0f);
        }
        else if (metadata == 2) // Attached to East wall (pointing West)
        {
            ctx.DrawTorch(block, new Vec3D(pos.X + horizontalOffset, pos.Y + verticalOffset, pos.Z), tiltAmount, 0.0f);
        }
        else if (metadata == 3) // Attached to North wall (pointing South)
        {
            ctx.DrawTorch(block, new Vec3D(pos.X, pos.Y + verticalOffset, pos.Z - horizontalOffset), 0.0f, -tiltAmount);
        }
        else if (metadata == 4) // Attached to South wall (pointing North)
        {
            ctx.DrawTorch(block, new Vec3D(pos.X, pos.Y + verticalOffset, pos.Z + horizontalOffset), 0.0f, tiltAmount);
        }
        else // Standing on floor
        {
            ctx.DrawTorch(block, new Vec3D(pos.X, pos.Y, pos.Z), 0.0f, 0.0f);
        }

        return true;
    }
}
