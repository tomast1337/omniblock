using java.io;

namespace betareborn.Network.Packets.Play
{
    public class ChatMessagePacket : Packet
    {
        public static readonly new java.lang.Class Class = ikvm.runtime.Util.getClassFromTypeHandle(typeof(ChatMessagePacket).TypeHandle);

        public string chatMessage;

        public ChatMessagePacket()
        {
        }

        public ChatMessagePacket(string var1)
        {
            if (var1.Length > 119)
            {
                var1 = var1.Substring(0, 119);
            }

            chatMessage = var1;
        }

        public override void read(DataInputStream var1)
        {
            chatMessage = readString(var1, 119);
        }

        public override void write(DataOutputStream var1)
        {
            writeString(chatMessage, var1);
        }

        public override void apply(NetHandler var1)
        {
            var1.onChatMessage(this);
        }

        public override int size()
        {
            return chatMessage.Length;
        }
    }

}