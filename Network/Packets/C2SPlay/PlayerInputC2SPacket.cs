using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.C2SPlay
{
    public class PlayerInputC2SPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayerInputC2SPacket).TypeHandle);

        private float field_22039_a;
        private float field_22038_b;
        private bool field_22043_c;
        private bool field_22042_d;
        private float field_22041_e;
        private float field_22040_f;

        public override void read(DataInputStream var1)
        {
            field_22039_a = var1.readFloat();
            field_22038_b = var1.readFloat();
            field_22041_e = var1.readFloat();
            field_22040_f = var1.readFloat();
            field_22043_c = var1.readBoolean();
            field_22042_d = var1.readBoolean();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeFloat(field_22039_a);
            var1.writeFloat(field_22038_b);
            var1.writeFloat(field_22041_e);
            var1.writeFloat(field_22040_f);
            var1.writeBoolean(field_22043_c);
            var1.writeBoolean(field_22042_d);
        }

        public override void apply(NetHandler var1)
        {
            var1.func_22185_a(this);
        }

        public override int size()
        {
            return 18;
        }
    }

}