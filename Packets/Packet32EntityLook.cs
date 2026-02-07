using java.io;

namespace betareborn.Packets
{
    public class Packet32EntityLook : Packet30Entity
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet32EntityLook).TypeHandle);

        public Packet32EntityLook()
        {
            this.rotating = true;
        }

        public override void read(DataInputStream var1)
        {
            base.read(var1);
            this.yaw = (sbyte)var1.readByte();
            this.pitch = (sbyte)var1.readByte();
        }

        public override void write(DataOutputStream var1)
        {
            base.write(var1);
            var1.writeByte(this.yaw);
            var1.writeByte(this.pitch);
        }

        public override int size()
        {
            return 6;
        }
    }

}