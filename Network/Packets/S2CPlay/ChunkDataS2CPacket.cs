using betareborn.Worlds;
using java.io;
using java.util.zip;

namespace betareborn.Network.Packets.S2CPlay
{
    public class ChunkDataS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ChunkDataS2CPacket).TypeHandle);

        public int x;
        public int y;
        public int z;
        public int sizeX;
        public int sizeY;
        public int sizeZ;
        public byte[] chunkData;
        private int chunkDataSize;

        public ChunkDataS2CPacket()
        {
            worldPacket = true;
        }

        public ChunkDataS2CPacket(int x, int y, int z, int sizeX, int sizeY, int sizeZ, World world)
        {
            worldPacket = true;
            this.x = x;
            this.y = y;
            this.z = z;
            this.sizeX = sizeX;
            this.sizeY = sizeY;
            this.sizeZ = sizeZ;
            byte[] var8 = world.getChunkData(x, y, z, sizeX, sizeY, sizeZ);
            Deflater var9 = new(-1);

            try
            {
                var9.setInput(var8);
                var9.finish();
                chunkData = new byte[sizeX * sizeY * sizeZ * 5 / 2];
                chunkDataSize = var9.deflate(chunkData);
            }
            finally
            {
                var9.end();
            }
        }

        public override void read(DataInputStream var1)
        {
            x = var1.readInt();
            y = var1.readShort();
            z = var1.readInt();
            sizeX = var1.read() + 1;
            sizeY = var1.read() + 1;
            sizeZ = var1.read() + 1;
            chunkDataSize = var1.readInt();
            byte[]
            var2 = new byte[chunkDataSize];
            var1.readFully(var2);

            chunkData = new byte[sizeX * sizeY * sizeZ * 5 / 2];
            Inflater var3 = new Inflater();
            var3.setInput(var2);

            try
            {
                var3.inflate(chunkData);
            }
            catch (DataFormatException var8)
            {
                throw new java.io.IOException("Bad compressed data format");
            }
            finally
            {
                var3.end();
            }

        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(x);
            var1.writeShort(y);
            var1.writeInt(z);
            var1.write(sizeX - 1);
            var1.write(sizeY - 1);
            var1.write(sizeZ - 1);
            var1.writeInt(chunkDataSize);
            var1.write(chunkData, 0, chunkDataSize);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleChunkData(this);
        }

        public override int size()
        {
            return 17 + chunkDataSize;
        }
    }

}