using betareborn.Entities;
using betareborn.Network.Packets;
using betareborn.Util.Maths;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntityPositionS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityPositionS2CPacket).TypeHandle);

        public int entityId;
        public int xPosition;
        public int yPosition;
        public int zPosition;
        public sbyte yaw;
        public sbyte pitch;

        public EntityPositionS2CPacket()
        {
        }

        public EntityPositionS2CPacket(Entity var1)
        {
            entityId = var1.entityId;
            xPosition = MathHelper.floor_double(var1.x * 32.0D);
            yPosition = MathHelper.floor_double(var1.y * 32.0D);
            zPosition = MathHelper.floor_double(var1.z * 32.0D);
            yaw = (sbyte)(int)(var1.yaw * 256.0F / 360.0F);
            pitch = (sbyte)(int)(var1.pitch * 256.0F / 360.0F);
        }

        public override void read(DataInputStream var1)
        {
            entityId = var1.readInt();
            xPosition = var1.readInt();
            yPosition = var1.readInt();
            zPosition = var1.readInt();
            yaw = (sbyte)var1.read();
            pitch = (sbyte)var1.read();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(entityId);
            var1.writeInt(xPosition);
            var1.writeInt(yPosition);
            var1.writeInt(zPosition);
            var1.write(yaw);
            var1.write(pitch);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleEntityTeleport(this);
        }

        public override int size()
        {
            return 34;
        }
    }

}