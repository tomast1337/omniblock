using betareborn.Network.Packets;
using java.io;
using java.util.zip;

namespace betareborn.Network.Packets.S2CPlay
{
    public class ChunkDataS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ChunkDataS2CPacket).TypeHandle);

        public int xPosition;
        public int yPosition;
        public int zPosition;
        public int xSize;
        public int ySize;
        public int zSize;
        public byte[] chunk;
        private int chunkSize;

        public ChunkDataS2CPacket()
        {
            worldPacket = true;
        }

        public override void read(DataInputStream var1)
        {
            xPosition = var1.readInt();
            yPosition = var1.readShort();
            zPosition = var1.readInt();
            xSize = var1.read() + 1;
            ySize = var1.read() + 1;
            zSize = var1.read() + 1;
            chunkSize = var1.readInt();
            byte[]
            var2 = new byte[chunkSize];
            var1.readFully(var2);

            chunk = new byte[xSize * ySize * zSize * 5 / 2];
            Inflater var3 = new Inflater();
            var3.setInput(var2);

            try
            {
                var3.inflate(chunk);
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
            var1.writeInt(xPosition);
            var1.writeShort(yPosition);
            var1.writeInt(zPosition);
            var1.write(xSize - 1);
            var1.write(ySize - 1);
            var1.write(zSize - 1);
            var1.writeInt(chunkSize);
            var1.write(chunk, 0, chunkSize);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleMapChunk(this);
        }

        public override int size()
        {
            return 17 + chunkSize;
        }
    }

}