using System.Net.Sockets;
using java.io;

namespace BetaSharp.Network.Packets.S2CPlay;

public class EntityStatusS2CPacket : Packet
{
    public int entityId;
    public sbyte entityStatus;

    public EntityStatusS2CPacket()
    {
    }

    public EntityStatusS2CPacket(int entityId, byte status)
    {
        this.entityId = entityId;
        entityStatus = (sbyte)status;
    }

    public override void Read(NetworkStream stream)
    {
        entityId = stream.readInt();
        entityStatus = (sbyte)stream.readByte();
    }

    public override void Write(NetworkStream stream)
    {
        stream.writeInt(entityId);
        stream.writeByte(entityStatus);
    }

    public override void Apply(NetHandler handler)
    {
        handler.onEntityStatus(this);
    }

    public override int Size()
    {
        return 5;
    }
}
