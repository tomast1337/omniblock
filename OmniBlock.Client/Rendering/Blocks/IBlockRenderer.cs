using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds;

namespace OmniBlock.Client.Rendering.Blocks;

public interface IBlockRenderer
{
    bool Draw(Block block, in BlockPos pos, ref BlockRenderContext ctx);
}
