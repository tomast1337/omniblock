using java.io;

namespace betareborn.Packets
{
    public class Packet2Handshake : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet2Handshake).TypeHandle);

        public String username;

        public Packet2Handshake()
        {
        }

        public Packet2Handshake(String var1)
        {
            this.username = var1;
        }

        public override void read(DataInputStream var1)
        {
            this.username = readString(var1, 32);
        }

        public override void write(DataOutputStream var1)
        {
            writeString(this.username, var1);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleHandshake(this);
        }

        public override int size()
        {
            return 4 + this.username.Length + 4;
        }
    }

}