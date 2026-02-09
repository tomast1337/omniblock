using betareborn.Entities;
using betareborn.Network.Packets;
using betareborn.Util.Maths;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class GlobalEntitySpawnS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(GlobalEntitySpawnS2CPacket).TypeHandle);

        public int field_27054_a;
        public int field_27053_b;
        public int field_27057_c;
        public int field_27056_d;
        public int field_27055_e;

        public GlobalEntitySpawnS2CPacket()
        {
        }

        public GlobalEntitySpawnS2CPacket(Entity var1)
        {
            field_27054_a = var1.entityId;
            field_27053_b = MathHelper.floor_double(var1.x * 32.0D);
            field_27057_c = MathHelper.floor_double(var1.y * 32.0D);
            field_27056_d = MathHelper.floor_double(var1.z * 32.0D);
            if (var1 is EntityLightningBolt)
            {
                field_27055_e = 1;
            }

        }

        public override void read(DataInputStream var1)
        {
            field_27054_a = var1.readInt();
            field_27055_e = (sbyte)var1.readByte();
            field_27053_b = var1.readInt();
            field_27057_c = var1.readInt();
            field_27056_d = var1.readInt();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(field_27054_a);
            var1.writeByte(field_27055_e);
            var1.writeInt(field_27053_b);
            var1.writeInt(field_27057_c);
            var1.writeInt(field_27056_d);
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