using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntityRotateAndMoveRelativeS2CPacket : EntityS2CPacket
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityRotateAndMoveRelativeS2CPacket).TypeHandle);

        public EntityRotateAndMoveRelativeS2CPacket()
        {
            rotate = true;
        }

        public EntityRotateAndMoveRelativeS2CPacket(int entityId, byte deltaX, byte deltaY, byte deltaZ, byte yaw, byte pitch) : base(entityId)
        {
            this.deltaX = (sbyte)deltaX;
            this.deltaY = (sbyte)deltaY;
            this.deltaZ = (sbyte)deltaZ;
            this.yaw = (sbyte)yaw;
            this.pitch = (sbyte)pitch;
            rotate = true;
        }

        public override void read(DataInputStream var1)
        {
            base.read(var1);
            deltaX = (sbyte)var1.readByte();
            deltaY = (sbyte)var1.readByte();
            deltaZ = (sbyte)var1.readByte();
            yaw = (sbyte)var1.readByte();
            pitch = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            base.write(var1);
            var1.writeByte(deltaX);
            var1.writeByte(deltaY);
            var1.writeByte(deltaZ);
            var1.writeByte(yaw);
            var1.writeByte(pitch);
        }

        public override int size()
        {
            return 9;
        }
    }

}