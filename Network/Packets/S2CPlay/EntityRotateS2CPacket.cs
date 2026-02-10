using java.io;

namespace betareborn.Network.Packets.S2CPlay
{
    public class EntityRotateS2CPacket : EntityS2CPacket
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(EntityRotateS2CPacket).TypeHandle);

        public EntityRotateS2CPacket()
        {
            rotate = true;
        }

        public EntityRotateS2CPacket(int entityId, byte yaw, byte pitch) : base(entityId)
        {
            this.yaw = (sbyte)yaw;
            this.pitch = (sbyte)pitch;
            rotate = true;
        }

        public override void read(DataInputStream var1)
        {
            base.read(var1);
            yaw = (sbyte)var1.readByte();
            pitch = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            base.write(var1);
            var1.writeByte(yaw);
            var1.writeByte(pitch);
        }

        public override int size()
        {
            return 6;
        }
    }

}