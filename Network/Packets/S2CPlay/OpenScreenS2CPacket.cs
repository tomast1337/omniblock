using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class OpenScreenS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(OpenScreenS2CPacket).TypeHandle);

        public int windowId;
        public int inventoryType;
        public string windowTitle;
        public int slotsCount;

        public override void apply(NetHandler var1)
        {
            var1.func_20087_a(this);
        }

        public override void read(DataInputStream var1)
        {
            windowId = (sbyte)var1.readByte();
            inventoryType = (sbyte)var1.readByte();
            windowTitle = var1.readUTF();
            slotsCount = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(windowId);
            var1.writeByte(inventoryType);
            var1.writeUTF(windowTitle);
            var1.writeByte(slotsCount);
        }

        public override int size()
        {
            return 3 + windowTitle.Length;
        }
    }

}