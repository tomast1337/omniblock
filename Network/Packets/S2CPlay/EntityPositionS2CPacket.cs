using betareborn.Entities;
using betareborn.Util.Maths;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntityPositionS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityPositionS2CPacket).TypeHandle);

        public int id;
        public int x;
        public int y;
        public int z;
        public sbyte yaw;
        public sbyte pitch;

        public EntityPositionS2CPacket()
        {
        }

        public EntityPositionS2CPacket(int entityId, int x, int y, int z, byte yaw, byte pitch)
        {
            this.id = entityId;
            this.x = x;
            this.y = y;
            this.z = z;
            this.yaw = (sbyte)yaw;
            this.pitch = (sbyte)pitch;
        }

        public EntityPositionS2CPacket(Entity var1)
        {
            id = var1.id;
            x = MathHelper.floor_double(var1.x * 32.0D);
            y = MathHelper.floor_double(var1.y * 32.0D);
            z = MathHelper.floor_double(var1.z * 32.0D);
            yaw = (sbyte)(int)(var1.yaw * 256.0F / 360.0F);
            pitch = (sbyte)(int)(var1.pitch * 256.0F / 360.0F);
        }

        public override void read(DataInputStream var1)
        {
            id = var1.readInt();
            x = var1.readInt();
            y = var1.readInt();
            z = var1.readInt();
            yaw = (sbyte)var1.read();
            pitch = (sbyte)var1.read();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(id);
            var1.writeInt(x);
            var1.writeInt(y);
            var1.writeInt(z);
            var1.write(yaw);
            var1.write(pitch);
        }

        public override void apply(NetHandler var1)
        {
            var1.onEntityPosition(this);
        }

        public override int size()
        {
            return 34;
        }
    }

}