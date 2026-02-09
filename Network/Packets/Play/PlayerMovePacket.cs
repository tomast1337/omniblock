using java.io;

namespace betareborn.Network.Packets.Play
{

    public class PlayerMovePacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayerMovePacket).TypeHandle);

        public double x;
        public double y;
        public double z;
        public double eyeHeight;
        public float yaw;
        public float pitch;
        public bool onGround;
        public bool changePosition;
        public bool changeLook;

        public PlayerMovePacket()
        {
        }

        public PlayerMovePacket(bool var1)
        {
            onGround = var1;
        }

        public override void apply(NetHandler var1)
        {
            var1.onPlayerMove(this);
        }

        public override void read(DataInputStream var1)
        {
            onGround = var1.read() != 0;
        }

        public override void write(DataOutputStream var1)
        {
            var1.write(onGround ? 1 : 0);
        }

        public override int size()
        {
            return 1;
        }
    }

}