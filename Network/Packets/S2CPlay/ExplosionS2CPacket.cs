using betareborn.Util.Maths;
using java.io;
using java.util;

namespace betareborn.Network.Packets.S2CPlay
{
    public class ExplosionS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ExplosionS2CPacket).TypeHandle);

        public double explosionX;
        public double explosionY;
        public double explosionZ;
        public float explosionSize;
        public Set destroyedBlockPositions;

        public ExplosionS2CPacket()
        {
        }

        public ExplosionS2CPacket(double x, double y, double z, float radius, Set affectedBlocks)
        {
            explosionX = x;
            explosionY = y;
            explosionZ = z;
            explosionSize = radius;
            destroyedBlockPositions = new HashSet(affectedBlocks);
        }

        public override void read(DataInputStream var1)
        {
            explosionX = var1.readDouble();
            explosionY = var1.readDouble();
            explosionZ = var1.readDouble();
            explosionSize = var1.readFloat();
            int var2 = var1.readInt();
            destroyedBlockPositions = new HashSet();
            int var3 = (int)explosionX;
            int var4 = (int)explosionY;
            int var5 = (int)explosionZ;

            for (int var6 = 0; var6 < var2; ++var6)
            {
                int var7 = (sbyte)var1.readByte() + var3;
                int var8 = (sbyte)var1.readByte() + var4;
                int var9 = (sbyte)var1.readByte() + var5;

                destroyedBlockPositions.add(new BlockPos(var7, var8, var9));
            }

        }

        public override void write(DataOutputStream var1)
        {
            var1.writeDouble(explosionX);
            var1.writeDouble(explosionY);
            var1.writeDouble(explosionZ);
            var1.writeFloat(explosionSize);
            var1.writeInt(destroyedBlockPositions.size());
            int var2 = (int)explosionX;
            int var3 = (int)explosionY;
            int var4 = (int)explosionZ;
            Iterator var5 = destroyedBlockPositions.iterator();

            while (var5.hasNext())
            {
                BlockPos var6 = (BlockPos)var5.next();
                int var7 = var6.x - var2;
                int var8 = var6.y - var3;
                int var9 = var6.z - var4;
                var1.writeByte(var7);
                var1.writeByte(var8);
                var1.writeByte(var9);
            }

        }

        public override void apply(NetHandler var1)
        {
            var1.onExplosion(this);
        }

        public override int size()
        {
            return 32 + destroyedBlockPositions.size() * 3;
        }
    }

}