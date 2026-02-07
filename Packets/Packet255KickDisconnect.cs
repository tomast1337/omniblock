using java.io;

namespace betareborn.Packets
{
    public class Packet255KickDisconnect : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(Packet255KickDisconnect).TypeHandle);

        public String reason;

        public Packet255KickDisconnect()
        {
        }

        public Packet255KickDisconnect(String var1)
        {
            this.reason = var1;
        }

        public override void read(DataInputStream var1)
        {
            this.reason = readString(var1, 100);
        }

        public override void write(DataOutputStream var1)
        {
            writeString(this.reason, var1);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleKickDisconnect(this);
        }

        public override int size()
        {
            return this.reason.Length;
        }
    }

}