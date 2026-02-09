using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntityMoveRelativeS2CPacket : EntityS2CPacket
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityMoveRelativeS2CPacket).TypeHandle);

        public override void read(DataInputStream var1)
        {
            base.read(var1);
            xPosition = (sbyte)var1.readByte();
            yPosition = (sbyte)var1.readByte();
            zPosition = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            base.write(var1);
            var1.writeByte(xPosition);
            var1.writeByte(yPosition);
            var1.writeByte(zPosition);
        }

        public override int size()
        {
            return 7;
        }
    }

}