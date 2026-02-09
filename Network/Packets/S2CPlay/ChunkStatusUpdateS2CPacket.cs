using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class ChunkStatusUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ChunkStatusUpdateS2CPacket).TypeHandle);

        public int xPosition;
        public int yPosition;
        public bool mode;

        public ChunkStatusUpdateS2CPacket()
        {
            worldPacket = false;
        }

        public override void read(DataInputStream var1)
        {
            xPosition = var1.readInt();
            yPosition = var1.readInt();
            mode = var1.read() != 0;
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(xPosition);
            var1.writeInt(yPosition);
            var1.write(mode ? 1 : 0);
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