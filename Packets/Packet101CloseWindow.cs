using java.io;

namespace betareborn.Packets
{
    public class Packet101CloseWindow : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet101CloseWindow).TypeHandle);

        public int windowId;

        public Packet101CloseWindow()
        {
        }

        public Packet101CloseWindow(int var1)
        {
            this.windowId = var1;
        }

        public override void apply(NetHandler var1)
        {
            var1.func_20092_a(this);
        }

        public override void read(DataInputStream var1)
        {
            this.windowId = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeByte(this.windowId);
        }

        public override int size()
        {
            return 1;
        }
    }

}