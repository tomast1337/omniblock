using java.io;

namespace betareborn.Network.Packets.C2SPlay
{
    public class PlayerInputC2SPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(PlayerInputC2SPacket).TypeHandle);

        private float sideways;
        private float forward;
        private bool jumping;
        private bool sneaking;
        private float pitch;
        private float yaw;

        public override void read(DataInputStream var1)
        {
            sideways = var1.readFloat();
            forward = var1.readFloat();
            pitch = var1.readFloat();
            yaw = var1.readFloat();
            jumping = var1.readBoolean();
            sneaking = var1.readBoolean();
        }

        public override void write(DataOutputStream var1)
        {
            var1.writeFloat(sideways);
            var1.writeFloat(forward);
            var1.writeFloat(pitch);
            var1.writeFloat(yaw);
            var1.writeBoolean(jumping);
            var1.writeBoolean(sneaking);
        }

        public override void apply(NetHandler var1)
        {
            var1.onPlayerInput(this);
        }

        public override int size()
        {
            return 18;
        }

        public float getSideways()
        {
            return sideways;
        }

        public float getPitch()
        {
            return pitch;
        }

        public float getForward()
        {
            return forward;
        }

        public float getYaw()
        {
            return yaw;
        }

        public bool isJumping()
        {
            return jumping;
        }

        public bool isSneaking()
        {
            return sneaking;
        }
    }

}