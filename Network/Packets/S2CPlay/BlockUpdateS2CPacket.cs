using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class BlockUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(BlockUpdateS2CPacket).TypeHandle);

        public int xPosition;
        public int yPosition;
        public int zPosition;
        public int type;
        public int metadata;

        public BlockUpdateS2CPacket()
        {
            worldPacket = true;
        }

        public override void read(DataInputStream var1)
        {
            xPosition = var1.readInt();
            yPosition = var1.read();
            zPosition = var1.readInt();
            type = var1.read();
            metadata = var1.read();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(xPosition);
            var1.write(yPosition);
            var1.writeInt(zPosition);
            var1.write(type);
            var1.write(metadata);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleBlockChange(this);
        }

        public override int size()
        {
            return 11;
        }
    }

}