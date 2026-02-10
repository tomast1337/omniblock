using java.io;

namespace betareborn.Network.Packets.S2CPlay
{

    public class WorldEventS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(WorldEventS2CPacket).TypeHandle);

        public int eventId;
        public int data;
        public int x;
        public int y;
        public int z;

        public WorldEventS2CPacket()
        {
        }

        public WorldEventS2CPacket(int eventId, int x, int y, int z, int data)
        {
            this.eventId = eventId;
            this.x = x;
            this.y = y;
            this.z = z;
            this.data = data;
        }

        public override void read(DataInputStream var1)
        {
            eventId = var1.readInt();
            x = var1.readInt();
            y = (sbyte)var1.readByte();
            z = var1.readInt();
            data = var1.readInt();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(eventId);
            var1.writeInt(x);
            var1.writeByte(y);
            var1.writeInt(z);
            var1.writeInt(data);
        }

        public override void apply(NetHandler var1)
        {
            var1.onWorldEvent(this);
        }

        public override int size()
        {
            return 20;
        }
    }

}