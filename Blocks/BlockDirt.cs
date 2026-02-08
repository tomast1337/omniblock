using betareborn.Blocks.Materials;

namespace betareborn.Blocks
{
    public class BlockDirt : Block
    {
        public BlockDirt(int id, int textureId) : base(id, textureId, Material.SOIL)
        {
        }
    }

}