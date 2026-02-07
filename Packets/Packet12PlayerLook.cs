using java.io;

namespace betareborn.Packets
{
    public class Packet12PlayerLook : Packet10Flying
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet12PlayerLook).TypeHandle);

        public Packet12PlayerLook()
        {
            this.rotating = true;
        }

        public Packet12PlayerLook(float var1, float var2, bool var3)
        {
            this.yaw = var1;
            this.pitch = var2;
            this.onGround = var3;
            this.rotating = true;
        }

        public override void read(DataInputStream var1)
        {
            this.yaw = var1.readFloat();
            this.pitch = var1.readFloat();
            base.read(var1);
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeFloat(this.yaw);
            var1.writeFloat(this.pitch);
            base.write(var1);
        }

        public override int size()
        {
            return 9;
        }
    }

}