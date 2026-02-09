using java.io;

namespace betareborn.Network.Packets
{
    public class HandshakePacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(HandshakePacket).TypeHandle);

        public string username;

        public HandshakePacket()
        {
        }

        public HandshakePacket(string var1)
        {
            username = var1;
        }

        public override void read(DataInputStream var1)
        {
            username = readString(var1, 32);
        }

        public override void write(DataOutputStream var1)
        {
            writeString(username, var1);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleHandshake(this);
        }

        public override int size()
        {
            return 4 + username.Length + 4;
        }
    }

}