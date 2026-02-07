using betareborn.Entities;
using java.io;

namespace betareborn.Packets
{
    public class Packet34EntityTeleport : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet34EntityTeleport).TypeHandle);

        public int entityId;
        public int xPosition;
        public int yPosition;
        public int zPosition;
        public sbyte yaw;
        public sbyte pitch;

        public Packet34EntityTeleport()
        {
        }

        public Packet34EntityTeleport(Entity var1)
        {
            this.entityId = var1.entityId;
            this.xPosition = MathHelper.floor_double(var1.posX * 32.0D);
            this.yPosition = MathHelper.floor_double(var1.posY * 32.0D);
            this.zPosition = MathHelper.floor_double(var1.posZ * 32.0D);
            this.yaw = (sbyte)((int)(var1.rotationYaw * 256.0F / 360.0F));
            this.pitch = (sbyte)((int)(var1.rotationPitch * 256.0F / 360.0F));
        }

        public override void read(DataInputStream var1)
        {
            this.entityId = var1.readInt();
            this.xPosition = var1.readInt();
            this.yPosition = var1.readInt();
            this.zPosition = var1.readInt();
            this.yaw = (sbyte)var1.read();
            this.pitch = (sbyte)var1.read();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeInt(this.entityId);
            var1.writeInt(this.xPosition);
            var1.writeInt(this.yPosition);
            var1.writeInt(this.zPosition);
            var1.write(this.yaw);
            var1.write(this.pitch);
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