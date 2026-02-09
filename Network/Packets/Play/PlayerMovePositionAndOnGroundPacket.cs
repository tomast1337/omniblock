using java.io;

namespace betareborn.Network.Packets.Play
{
    public class PlayerMovePositionAndOnGroundPacket : PlayerMovePacket
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayerMovePositionAndOnGroundPacket).TypeHandle);

        public PlayerMovePositionAndOnGroundPacket()
        {
            changePosition = true;
        }

        public PlayerMovePositionAndOnGroundPacket(double var1, double var3, double var5, double var7, bool var9)
        {
            x = var1;
            y = var3;
            eyeHeight = var5;
            z = var7;
            onGround = var9;
            changePosition = true;
        }

        public override void read(DataInputStream var1)
        {
            x = var1.readDouble();
            y = var1.readDouble();
            eyeHeight = var1.readDouble();
            z = var1.readDouble();
            base.read(var1);
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeDouble(x);
            var1.writeDouble(y);
            var1.writeDouble(eyeHeight);
            var1.writeDouble(z);
            base.write(var1);
        }

        public override int size()
        {
            return 33;
        }
    }

}