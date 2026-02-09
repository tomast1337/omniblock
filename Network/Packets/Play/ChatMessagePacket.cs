using betareborn.Network.Packets;
using java.io;

namespace betareborn.Network.Packets.Play
{
    public class ChatMessagePacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ChatMessagePacket).TypeHandle);

        public string message;

        public ChatMessagePacket()
        {
        }

        public ChatMessagePacket(string var1)
        {
            if (var1.Length > 119)
            {
                var1 = var1.Substring(0, 119);
            }

            message = var1;
        }

        public override void read(DataInputStream var1)
        {
            message = readString(var1, 119);
        }

        public override void write(DataOutputStream var1)
        {
            writeString(message, var1);
        }

        public override void apply(NetHandler var1)
        {
            var1.handleChat(this);
        }

        public override int size()
        {
            return message.Length;
        }
    }

}