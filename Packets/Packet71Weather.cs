using betareborn.Entities;
using java.io;

namespace betareborn.Packets
{
    public class Packet71Weather : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet71Weather).TypeHandle);

        public int field_27054_a;
        public int field_27053_b;
        public int field_27057_c;
        public int field_27056_d;
        public int field_27055_e;

        public Packet71Weather()
        {
        }

        public Packet71Weather(Entity var1)
        {
            this.field_27054_a = var1.entityId;
            this.field_27053_b = MathHelper.floor_double(var1.posX * 32.0D);
            this.field_27057_c = MathHelper.floor_double(var1.posY * 32.0D);
            this.field_27056_d = MathHelper.floor_double(var1.posZ * 32.0D);
            if (var1 is EntityLightningBolt)
            {
                this.field_27055_e = 1;
            }

        }

        public override void read(DataInputStream var1)
        {
            this.field_27054_a = var1.readInt();
            this.field_27055_e = (sbyte)var1.readByte();
            this.field_27053_b = var1.readInt();
            this.field_27057_c = var1.readInt();
            this.field_27056_d = var1.readInt();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.field_27054_a);
            var1.writeByte(this.field_27055_e);
            var1.writeInt(this.field_27053_b);
            var1.writeInt(this.field_27057_c);
            var1.writeInt(this.field_27056_d);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleWeather(this);
        }

        public override int size()
        {
            return 17;
        }
    }

}