using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntityRotateAndMoveRelativeS2CPacket : EntityS2CPacket
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityRotateAndMoveRelativeS2CPacket).TypeHandle);

        public EntityRotateAndMoveRelativeS2CPacket()
        {
            rotating = true;
        }

        public override void read(DataInputStream var1)
        {
            base.read(var1);
            xPosition = (sbyte)var1.readByte();
            yPosition = (sbyte)var1.readByte();
            zPosition = (sbyte)var1.readByte();
            yaw = (sbyte)var1.readByte();
            pitch = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            base.write(var1);
            var1.writeByte(xPosition);
            var1.writeByte(yPosition);
            var1.writeByte(zPosition);
            var1.writeByte(yaw);
            var1.writeByte(pitch);
        }

        public override int size()
        {
            return 9;
        }
    }

}