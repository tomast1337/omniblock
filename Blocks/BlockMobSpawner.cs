using betareborn.Materials;
using betareborn.TileEntities;

namespace betareborn.Blocks
{
    public class BlockMobSpawner : BlockContainer
    {

        public BlockMobSpawner(int var1, int var2) : base(var1, var2, Material.STONE)
        {
        }

        protected override TileEntity getBlockEntity()
        {
            return new TileEntityMobSpawner();
        }

        public override int idDropped(int var1, java.util.Random var2)
        {
            return 0;
        }

        public override int quantityDropped(java.util.Random var1)
        {
            return 0;
        }

        public override bool isOpaqueCube()
        {
            return false;
        }
    }

}