using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class ChunkStatusUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ChunkStatusUpdateS2CPacket).TypeHandle);

        public int x;
        public int z;
        public bool load;

        public ChunkStatusUpdateS2CPacket()
        {
            worldPacket = false;
        }

        public ChunkStatusUpdateS2CPacket(int x, int z, bool load)
        {
            worldPacket = false;
            this.x = x;
            this.z = z;
            this.load = load;
        }

        public override void read(DataInputStream var1)
        {
            x = var1.readInt();
            z = var1.readInt();
            load = var1.read() != 0;
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(x);
            var1.writeInt(z);
            var1.write(load ? 1 : 0);
        }

        public override void apply(NetHandler var1)
        {
            var1.onChunkStatusUpdate(this);
        }

        public override int size()
        {
            return 9;
        }
    }

}