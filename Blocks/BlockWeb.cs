using betareborn.Entities;
using betareborn.Items;
using betareborn.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockWeb : Block
    {
        public BlockWeb(int id, int texturePosition) : base(id, texturePosition, Material.COBWEB)
        {
        }

        public override void onEntityCollision(World world, int x, int y, int z, Entity entity)
        {
            entity.isInWeb = true;
        }

        public override bool isOpaque()
        {
            return false;
        }

        public override Box getCollisionShape(World world, int x, int y, int z)
        {
            return null;
        }

        public override int getRenderType()
        {
            return 1;
        }

        public override bool isFullCube()
        {
            return false;
        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return Item.silk.id;
        }
    }

}