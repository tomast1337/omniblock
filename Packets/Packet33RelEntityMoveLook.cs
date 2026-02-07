using java.io;

namespace betareborn.Packets
{
    public class Packet33RelEntityMoveLook : Packet30Entity
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet33RelEntityMoveLook).TypeHandle);

        public Packet33RelEntityMoveLook()
        {
            this.rotating = true;
        }

        public override void read(DataInputStream var1)
        {
            base.read(var1);
            this.xPosition = (sbyte)var1.readByte();
            this.yPosition = (sbyte)var1.readByte();
            this.zPosition = (sbyte)var1.readByte();
            this.yaw = (sbyte)var1.readByte();
            this.pitch = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            base.write(var1);
            var1.writeByte(this.xPosition);
            var1.writeByte(this.yPosition);
            var1.writeByte(this.zPosition);
            var1.writeByte(this.yaw);
            var1.writeByte(this.pitch);
        }

        public override int size()
        {
            return 9;
        }
    }

}