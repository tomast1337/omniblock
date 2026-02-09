using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.Play
{
    public class DisconnectPacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(DisconnectPacket).TypeHandle);

        public string reason;

        public DisconnectPacket()
        {
        }

        public DisconnectPacket(string var1)
        {
            reason = var1;
        }

        public override void read(DataInputStream var1)
        {
            reason = readString(var1, 100);
        }

        public override void write(DataOutputStream var1)
        {
            writeString(reason, var1);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleKickDisconnect(this);
        }

        public override int size()
        {
            return reason.Length;
        }
    }

}