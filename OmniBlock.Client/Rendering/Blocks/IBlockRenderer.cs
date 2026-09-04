using OmniBlock.Blocks;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Blocks;

public interface IBlockRenderer
{
    bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx);
}
