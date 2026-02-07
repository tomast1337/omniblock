using betareborn.Entities;

namespace betareborn
{
    public class HitResult : java.lang.Object
    {
        public EnumMovingObjectType typeOfHit;
        public int blockX;
        public int blockY;
        public int blockZ;
        public int sideHit;
        public Vec3D hitVec;
        public Entity entityHit;

        public HitResult(int var1, int var2, int var3, int var4, Vec3D var5)
        {
            typeOfHit = EnumMovingObjectType.TILE;
            blockX = var1;
            blockY = var2;
            blockZ = var3;
            sideHit = var4;
            hitVec = Vec3D.createVector(var5.xCoord, var5.yCoord, var5.zCoord);
        }

        public HitResult(Entity var1)
        {
            typeOfHit = EnumMovingObjectType.ENTITY;
            entityHit = var1;
            hitVec = Vec3D.createVector(var1.posX, var1.posY, var1.posZ);
        }
    }

}