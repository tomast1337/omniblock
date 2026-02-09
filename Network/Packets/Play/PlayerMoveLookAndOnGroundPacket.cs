using java.io;

namespace betareborn.Network.Packets.Play
{
    public class PlayerMoveLookAndOnGroundPacket : PlayerMovePacket
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayerMoveLookAndOnGroundPacket).TypeHandle);

        public PlayerMoveLookAndOnGroundPacket()
        {
            rotating = true;
        }

        public PlayerMoveLookAndOnGroundPacket(float var1, float var2, bool var3)
        {
            yaw = var1;
            pitch = var2;
            onGround = var3;
            rotating = true;
        }

        public override void read(DataInputStream var1)
        {
            yaw = var1.readFloat();
            pitch = var1.readFloat();
            base.read(var1);
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeFloat(yaw);
            var1.writeFloat(pitch);
            base.write(var1);
        }

        public override int size()
        {
            return 9;
        }
    }

}