using betareborn.Blocks.Materials;

namespace betareborn.Blocks
{
    public class BlockOreStorage : Block
    {

        public BlockOreStorage(int id, int textureId) : base(id, Material.METAL)
        {
            base.textureId = textureId;
        }

        public override int getTexture(int side)
        {
            return textureId;
        }
    }

}