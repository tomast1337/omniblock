using java.io;

namespace betareborn.Packets
{
    public class Packet11PlayerPosition : Packet10Flying
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet11PlayerPosition).TypeHandle);

        public Packet11PlayerPosition()
        {
            this.moving = true;
        }

        public Packet11PlayerPosition(double var1, double var3, double var5, double var7, bool var9)
        {
            this.xPosition = var1;
            this.yPosition = var3;
            this.stance = var5;
            this.zPosition = var7;
            this.onGround = var9;
            this.moving = true;
        }

        public override void read(DataInputStream var1)
        {
            this.xPosition = var1.readDouble();
            this.yPosition = var1.readDouble();
            this.stance = var1.readDouble();
            this.zPosition = var1.readDouble();
            base.read(var1);
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeDouble(this.xPosition);
            var1.writeDouble(this.yPosition);
            var1.writeDouble(this.stance);
            var1.writeDouble(this.zPosition);
            base.write(var1);
        }

        public override int size()
        {
            return 33;
        }
    }

}