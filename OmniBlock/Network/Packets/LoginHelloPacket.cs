namespace OmniBlock.Network.Packets;

public class LoginHelloPacket() : Packet(PacketId.LoginHello)
{
    public int ProtocolVersion { get; private set; }
    public string Username { get; set; } = "";

    /// <summary>
    ///     Server to client, the world seed. Client to server, the field is meaningless — the client
    ///     cannot know a seed it is about to be told — so it carries the OmniBlock capability
    ///     declaration instead. See <see cref="ProtocolHandshake" /> for why that is smuggled here
    ///     rather than sent as its own packet.
    /// </summary>
    public long WorldSeed { get; private set; }
    public sbyte DimensionId { get; private set; }

    public static LoginHelloPacket Get(string username, int protocolVersion, long worldSeed, sbyte dimensionId)
    {
        LoginHelloPacket p = Get<LoginHelloPacket>(PacketId.LoginHello);
        p.Username = username;
        p.ProtocolVersion = protocolVersion;
        p.WorldSeed = worldSeed;
        p.DimensionId = dimensionId;
        return p;
    }

    public override void Read(Stream stream)
    {
        ProtocolVersion = stream.ReadInt();
        Username = stream.ReadLongString(16);
        WorldSeed = stream.ReadLong();
        DimensionId = (sbyte)stream.ReadByte();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(ProtocolVersion);
        stream.WriteLongString(Username);
        stream.WriteLong(WorldSeed);
        stream.WriteByte((byte)DimensionId);
    }

    public override void Apply(NetHandler handler) => handler.onHello(this);

    public override int Size() =>
        sizeof(int) + StreamExtensions.LongStringSize(Username) + sizeof(long) + sizeof(byte);
}
