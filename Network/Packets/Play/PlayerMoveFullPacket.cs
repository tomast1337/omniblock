using java.io;

namespace betareborn.Network.Packets.Play
{
    public class PlayerMoveFullPacket : PlayerMovePacket
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayerMoveFullPacket).TypeHandle);

        public PlayerMoveFullPacket()
        {
            changeLook = true;
            changePosition = true;
        }

        public PlayerMoveFullPacket(double var1, double var3, double var5, double var7, float var9, float var10, bool var11)
        {
            x = var1;
            y = var3;
            eyeHeight = var5;
            z = var7;
            yaw = var9;
            pitch = var10;
            onGround = var11;
            changeLook = true;
            changePosition = true;
        }

        public override void read(DataInputStream var1)
        {
            x = var1.readDouble();
            y = var1.readDouble();
            eyeHeight = var1.readDouble();
            z = var1.readDouble();
            yaw = var1.readFloat();
            pitch = var1.readFloat();
            base.read(var1);
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeDouble(x);
            var1.writeDouble(y);
            var1.writeDouble(eyeHeight);
            var1.writeDouble(z);
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