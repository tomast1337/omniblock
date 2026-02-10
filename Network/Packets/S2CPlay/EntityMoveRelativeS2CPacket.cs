using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntityMoveRelativeS2CPacket : EntityS2CPacket
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityMoveRelativeS2CPacket).TypeHandle);

        public EntityMoveRelativeS2CPacket()
        {
        }

        public EntityMoveRelativeS2CPacket(int entityId, byte deltaX, byte deltaY, byte deltaZ) : base(entityId)
        {
            this.deltaX = (sbyte)deltaX;
            this.deltaY = (sbyte)deltaY;
            this.deltaZ = (sbyte)deltaZ;
        }

        public override void read(DataInputStream var1)
        {
            base.read(var1);
            deltaX = (sbyte)var1.readByte();
            deltaY = (sbyte)var1.readByte();
            deltaZ = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            base.write(var1);
            var1.writeByte(deltaX);
            var1.writeByte(deltaY);
            var1.writeByte(deltaZ);
        }

        public override int size()
        {
            return 7;
        }
    }

}