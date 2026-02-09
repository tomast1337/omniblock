using java.io;

namespace betareborn.Network.Packets.Play
{
    public class PlayerMoveFullPacket : PlayerMovePacket
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayerMoveFullPacket).TypeHandle);

        public PlayerMoveFullPacket()
        {
            rotating = true;
            moving = true;
        }

        public PlayerMoveFullPacket(double var1, double var3, double var5, double var7, float var9, float var10, bool var11)
        {
            xPosition = var1;
            yPosition = var3;
            stance = var5;
            zPosition = var7;
            yaw = var9;
            pitch = var10;
            onGround = var11;
            rotating = true;
            moving = true;
        }

        public override void read(DataInputStream var1)
        {
            xPosition = var1.readDouble();
            yPosition = var1.readDouble();
            stance = var1.readDouble();
            zPosition = var1.readDouble();
            yaw = var1.readFloat();
            pitch = var1.readFloat();
            base.read(var1);
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeDouble(xPosition);
            var1.writeDouble(yPosition);
            var1.writeDouble(stance);
            var1.writeDouble(zPosition);
            var1.writeFloat(yaw);
            var1.writeFloat(pitch);
            base.write(var1);
        }

        public override int size()
        {
            return 41;
        }
    }

}