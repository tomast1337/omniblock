using System.Net.Sockets;
using java.io;

namespace BetaSharp.Network.Packets.Play;

public class ChatMessagePacket : Packet
{
    public string chatMessage;

    public ChatMessagePacket()
    {
    }

    public ChatMessagePacket(string msg)
    {
        if (msg.Length > 119)
        {
            msg = msg.Substring(0, 119);
        }

        chatMessage = msg;
    }

    public override void Read(NetworkStream stream)
    {
        chatMessage = ReadString(stream, 119);
    }

    public override void Write(NetworkStream stream)
    {
        WriteString(chatMessage, stream);
    }

    public override void Apply(NetHandler handler)
    {
        handler.onChatMessage(this);
    }

    public override int Size()
    {
        return chatMessage.Length;
    }
}
