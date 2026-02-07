using java.io;
using java.util.zip;

namespace betareborn.Packets
{
    public class Packet51MapChunk : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet51MapChunk).TypeHandle);

        public int xPosition;
        public int yPosition;
        public int zPosition;
        public int xSize;
        public int ySize;
        public int zSize;
        public byte[] chunk;
        private int chunkSize;

        public Packet51MapChunk()
        {
            this.worldPacket = true;
        }

        public override void read(DataInputStream var1)
        {
            this.xPosition = var1.readInt();
            this.yPosition = var1.readShort();
            this.zPosition = var1.readInt();
            this.xSize = var1.read() + 1;
            this.ySize = var1.read() + 1;
            this.zSize = var1.read() + 1;
            this.chunkSize = var1.readInt();
            byte[]
            var2 = new byte[this.chunkSize];
            var1.readFully(var2);

            this.chunk = new byte[this.xSize * this.ySize * this.zSize * 5 / 2];
            Inflater var3 = new Inflater();
            var3.setInput(var2);

            try
            {
                var3.inflate(this.chunk);
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
            var1.writeInt(this.xPosition);
            var1.writeShort(this.yPosition);
            var1.writeInt(this.zPosition);
            var1.write(this.xSize - 1);
            var1.write(this.ySize - 1);
            var1.write(this.zSize - 1);
            var1.writeInt(this.chunkSize);
            var1.write(this.chunk, 0, this.chunkSize);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleMapChunk(this);
        }

        public override int size()
        {
            return 17 + this.chunkSize;
        }
    }

}