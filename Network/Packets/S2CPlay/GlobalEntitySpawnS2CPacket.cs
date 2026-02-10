using betareborn.Entities;
using betareborn.Util.Maths;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class GlobalEntitySpawnS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(GlobalEntitySpawnS2CPacket).TypeHandle);

        public int id;
        public int x;
        public int y;
        public int z;
        public int type;

        public GlobalEntitySpawnS2CPacket()
        {
        }

        public GlobalEntitySpawnS2CPacket(Entity var1)
        {
            id = var1.id;
            x = MathHelper.floor_double(var1.x * 32.0D);
            y = MathHelper.floor_double(var1.y * 32.0D);
            z = MathHelper.floor_double(var1.z * 32.0D);
            if (var1 is EntityLightningBolt)
            {
                type = 1;
            }

        }

        public override void read(DataInputStream var1)
        {
            id = var1.readInt();
            type = (sbyte)var1.readByte();
            x = var1.readInt();
            y = var1.readInt();
            z = var1.readInt();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(id);
            var1.writeByte(type);
            var1.writeInt(x);
            var1.writeInt(y);
            var1.writeInt(z);
        }

        public override void apply(NetHandler var1)
        {
            var1.onLightningEntitySpawn(this);
        }

        public override int size()
        {
            return 17;
        }
    }

}