using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class CloseScreenS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(CloseScreenS2CPacket).TypeHandle);

        public int windowId;

        public CloseScreenS2CPacket()
        {
        }

        public CloseScreenS2CPacket(int var1)
        {
            windowId = var1;
        }

        public override void apply(NetHandler var1)
        {
            var1.onCloseScreen(this);
        }

        public override void read(DataInputStream var1)
        {
            windowId = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(windowId);
        }

        public override int size()
        {
            return 1;
        }
    }

}