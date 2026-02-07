using java.io;

namespace betareborn.Packets
{
    public class Packet100OpenWindow : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet100OpenWindow).TypeHandle);

        public int windowId;
        public int inventoryType;
        public String windowTitle;
        public int slotsCount;

        public override void apply(NetHandler var1)
        {
            var1.func_20087_a(this);
        }

        public override void read(DataInputStream var1)
        {
            this.windowId = (sbyte)var1.readByte();
            this.inventoryType = (sbyte)var1.readByte();
            this.windowTitle = var1.readUTF();
            this.slotsCount = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(this.windowId);
            var1.writeByte(this.inventoryType);
            var1.writeUTF(this.windowTitle);
            var1.writeByte(this.slotsCount);
        }

        public override int size()
        {
            return 3 + this.windowTitle.Length;
        }
    }

}