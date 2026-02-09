using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.C2SPlay
{
    public class UpdateSelectedSlotC2SPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(UpdateSelectedSlotC2SPacket).TypeHandle);

        public int id;

        public UpdateSelectedSlotC2SPacket()
        {
        }

        public UpdateSelectedSlotC2SPacket(int var1)
        {
            id = var1;
        }

        public override void read(DataInputStream var1)
        {
            id = var1.readShort();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeShort(id);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleBlockItemSwitch(this);
        }

        public override int size()
        {
            return 2;
        }
    }

}