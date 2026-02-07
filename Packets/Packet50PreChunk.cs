using java.io;

namespace betareborn.Packets
{
    public class Packet50PreChunk : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet50PreChunk).TypeHandle);

        public int xPosition;
        public int yPosition;
        public bool mode;

        public Packet50PreChunk()
        {
            this.worldPacket = false;
        }

        public override void read(DataInputStream var1)
        {
            this.xPosition = var1.readInt();
            this.yPosition = var1.readInt();
            this.mode = var1.read() != 0;
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.xPosition);
            var1.writeInt(this.yPosition);
            var1.write(this.mode ? 1 : 0);
        }

        public override void apply(NetHandler var1)
        {
            var1.handlePreChunk(this);
        }

        public override int size()
        {
            return 9;
        }
    }

}