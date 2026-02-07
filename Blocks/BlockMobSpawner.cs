using betareborn.Materials;
using betareborn.TileEntities;

namespace betareborn.Blocks
{
    public class BlockMobSpawner : BlockContainer
    {

        public BlockMobSpawner(int id, int textureId) : base(id, textureId, Material.STONE)
        {
        }

        protected override TileEntity getBlockEntity()
        {
            return new TileEntityMobSpawner();
        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return 0;
        }

        public override int getDroppedItemCount(java.util.Random random)
        {
            return 0;
        }

        public override bool isOpaque()
        {
            return false;
        }
    }

}