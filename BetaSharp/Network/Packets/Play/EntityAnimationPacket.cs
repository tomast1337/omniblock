using System.Net.Sockets;
using BetaSharp.Entities;
using java.io;

namespace BetaSharp.Network.Packets.Play;

public class EntityAnimationPacket : Packet
{
    public int id;
    public int animationId;

    public EntityAnimationPacket()
    {
    }

    public EntityAnimationPacket(Entity ent, int animationId)
    {
        id = ent.id;
        this.animationId = animationId;
    }

    public override void Read(NetworkStream stream)
    {
        id = stream.readInt();
        animationId = (sbyte)stream.readByte();
    }

    public override void Write(NetworkStream stream)
    {
        stream.writeInt(id);
        stream.writeByte(animationId);
    }

    public override void Apply(NetHandler handler)
    {
        handler.onEntityAnimation(this);
    }

    public override int Size()
    {
        return 5;
    }
}
