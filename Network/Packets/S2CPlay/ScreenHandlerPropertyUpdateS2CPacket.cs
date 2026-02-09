using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class ScreenHandlerPropertyUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ScreenHandlerPropertyUpdateS2CPacket).TypeHandle);

        public int windowId;
        public int progressBar;
        public int progressBarValue;

        public override void apply(NetHandler var1)
        {
            var1.func_20090_a(this);
        }

        public override void read(DataInputStream var1)
        {
            windowId = (sbyte)var1.readByte();
            progressBar = var1.readShort();
            progressBarValue = var1.readShort();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(windowId);
            var1.writeShort(progressBar);
            var1.writeShort(progressBarValue);
        }

        public override int size()
        {
            return 5;
        }
    }

}