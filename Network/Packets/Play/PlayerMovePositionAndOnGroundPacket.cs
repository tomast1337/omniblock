using java.io;

namespace betareborn.Network.Packets.Play
{
    public class PlayerMovePositionAndOnGroundPacket : PlayerMovePacket
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayerMovePositionAndOnGroundPacket).TypeHandle);

        public PlayerMovePositionAndOnGroundPacket()
        {
            moving = true;
        }

        public PlayerMovePositionAndOnGroundPacket(double var1, double var3, double var5, double var7, bool var9)
        {
            xPosition = var1;
            yPosition = var3;
            stance = var5;
            zPosition = var7;
            onGround = var9;
            moving = true;
        }

        public override void read(DataInputStream var1)
        {
            xPosition = var1.readDouble();
            yPosition = var1.readDouble();
            stance = var1.readDouble();
            zPosition = var1.readDouble();
            base.read(var1);
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeDouble(xPosition);
            var1.writeDouble(yPosition);
            var1.writeDouble(stance);
            var1.writeDouble(zPosition);
            base.write(var1);
        }

        public override int size()
        {
            return 33;
        }
    }

}