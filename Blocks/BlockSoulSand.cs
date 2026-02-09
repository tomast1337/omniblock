using betareborn.Blocks.Materials;
using betareborn.Entities;
using betareborn.Util.Maths;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockSoulSand : Block
    {

        public BlockSoulSand(int id, int textureId) : base(id, textureId, Material.SAND)
        {
        }

        public override Box? getCollisionShape(World world, int x, int y, int z)
        {
            float var5 = 2.0F / 16.0F;
            return new Box((double)x, (double)y, (double)z, (double)(x + 1), (double)((float)(y + 1) - var5), (double)(z + 1));
        }

        public override void onEntityCollision(World world, int x, int y, int z, Entity entity)
        {
            entity.velocityX *= 0.4D;
            entity.velocityZ *= 0.4D;
        }
    }
}