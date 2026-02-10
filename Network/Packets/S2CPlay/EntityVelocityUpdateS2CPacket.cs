using betareborn.Entities;
using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntityVelocityUpdateS2CPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityVelocityUpdateS2CPacket).TypeHandle);

        public int entityId;
        public int motionX;
        public int motionY;
        public int motionZ;

        public EntityVelocityUpdateS2CPacket()
        {
        }

        public EntityVelocityUpdateS2CPacket(Entity var1) : this(var1.id, var1.velocityX, var1.velocityY, var1.velocityZ)
        {
        }

        public EntityVelocityUpdateS2CPacket(int var1, double var2, double var4, double var6)
        {
            entityId = var1;
            double var8 = 3.9D;
            if (var2 < -var8)
            {
                var2 = -var8;
            }

            if (var4 < -var8)
            {
                var4 = -var8;
            }

            if (var6 < -var8)
            {
                var6 = -var8;
            }

            if (var2 > var8)
            {
                var2 = var8;
            }

            if (var4 > var8)
            {
                var4 = var8;
            }

            if (var6 > var8)
            {
                var6 = var8;
            }

            motionX = (int)(var2 * 8000.0D);
            motionY = (int)(var4 * 8000.0D);
            motionZ = (int)(var6 * 8000.0D);
        }

        public override void read(DataInputStream var1)
        {
            entityId = var1.readInt();
            motionX = var1.readShort();
            motionY = var1.readShort();
            motionZ = var1.readShort();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(entityId);
            var1.writeShort(motionX);
            var1.writeShort(motionY);
            var1.writeShort(motionZ);
        }

        public override void apply(NetHandler var1)
        {
            var1.onEntityVelocityUpdate(this);
        }

        public override int size()
        {
            return 10;
        }
    }

}