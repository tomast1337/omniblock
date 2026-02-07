using java.io;

namespace betareborn.Packets
{
    public class Packet13PlayerLookMove : Packet10Flying
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet13PlayerLookMove).TypeHandle);

        public Packet13PlayerLookMove()
        {
            this.rotating = true;
            this.moving = true;
        }

        public Packet13PlayerLookMove(double var1, double var3, double var5, double var7, float var9, float var10, bool var11)
        {
            this.xPosition = var1;
            this.yPosition = var3;
            this.stance = var5;
            this.zPosition = var7;
            this.yaw = var9;
            this.pitch = var10;
            this.onGround = var11;
            this.rotating = true;
            this.moving = true;
        }

        public override void read(DataInputStream var1)
        {
            this.xPosition = var1.readDouble();
            this.yPosition = var1.readDouble();
            this.stance = var1.readDouble();
            this.zPosition = var1.readDouble();
            this.yaw = var1.readFloat();
            this.pitch = var1.readFloat();
            base.read(var1);
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeDouble(this.xPosition);
            var1.writeDouble(this.yPosition);
            var1.writeDouble(this.stance);
            var1.writeDouble(this.zPosition);
            var1.writeFloat(this.yaw);
            var1.writeFloat(this.pitch);
            base.write(var1);
        }

        public override int size()
        {
            return 41;
        }
    }

}