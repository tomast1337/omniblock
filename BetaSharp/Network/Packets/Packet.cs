using BetaSharp.Network.Packets.Play;
using BetaSharp.Network.Packets.S2CPlay;
using BetaSharp.Util;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Network.Packets;

public abstract class Packet
{
    public static readonly ObjectFactory<Packet, PacketRegisterItem> Registry = new(256);
    private static readonly ILogger<Packet> s_logger = Log.Instance.For<Packet>();

    private static readonly Dictionary<int, PacketTracker> s_trackers = new();

    public long CreationTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public readonly byte Id;

    protected Packet(byte id)
    {
        Id = id;
    }

    protected Packet(PacketId id)
    {
        Id = (byte)id;
    }

    public static T Get<T>(PacketId id) where T : Packet
    {
        Packet p = Get((byte)id);
        if (p is T p2)
        {
            return p2;
        }

        throw new Exception("Packet id " + id + " is not of type " + typeof(T));
    }

    public static Packet Get(PacketId id) => Get((byte)id);

    public static Packet Get(byte id)
    {
        if (!Registry.TryGet(id, out PacketRegisterItem? packetR))
        {
            throw new Exception("Unable to get packet id " + id);
        }

        return packetR.New();
    }

    public static Packet? Read(Stream stream, bool server)
    {
        Packet? packet;
        int rawId;
        try
        {
            rawId = stream.ReadByte();
            if (rawId == -1)
            {
                return null;
            }

            if (!Registry.TryGet(rawId, out PacketRegisterItem? packetR))
            {
                throw new IOException("Bad packet id " + rawId);
            }

            if (server)
            {
                if (!packetR.ServerBound)
                {
                    throw new IOException("Bad server bound packet id " + rawId);
                }
            }
            else
            {
                if (!packetR.ClientBound)
                {
                    throw new IOException("Bad client bound packet id " + rawId);
                }
            }

            packet = packetR.New();

            packet.Read(stream);
        }
        catch (IOException e)
        {
            s_logger.LogInformation("Reached end of stream : " + e.Message);
            return null;
        }

        if (!s_trackers.TryGetValue(rawId, out PacketTracker? tracker))
        {
            tracker = new PacketTracker();
            s_trackers.Add(rawId, tracker);
        }

        tracker.Update(packet.Size());

        return packet;
    }

    public static void Write(Packet packet, Stream stream)
    {
        stream.WriteByte(packet.Id);
        packet.Write(stream);
    }

    public abstract void Read(Stream stream);

    public abstract void Write(Stream stream);

    public abstract void Apply(NetHandler handler);

    public abstract int Size();

    public virtual void ProcessForInternal() { }

    static Packet() =>
        Registry.Register([
            New(PacketId.LoginHello, true, true, false, () => new LoginHelloPacket()),
            New(PacketId.Handshake, true, true, false, () => new HandshakePacket()),
            New(PacketId.PlayerMove, true, true, false, () => new PlayerMovePacket()),
            New(PacketId.PlayerMovePositionAndOnGround, true, true, false, () => new PlayerMovePositionAndOnGroundPacket()),
            New(PacketId.PlayerMoveLookAndOnGround, true, true, false, () => new PlayerMoveLookAndOnGroundPacket()),
            New(PacketId.PlayerMoveFull, true, true, false, () => new PlayerMoveFullPacket()),
            New(PacketId.ChunkDataS2C, true, false, true, () => new ChunkDataS2CPacket()),
            New(PacketId.MessageRegistrySyncS2C, true, false, false, () => new MessageRegistrySyncS2CPacket()),
            New(PacketId.OmniMessage, true, true, false, () => new OmniMessagePacket())
        ]);

    public class PacketRegisterItem(byte rawId, bool clientBound, bool serverBound, bool worldPacket, Func<Packet> factory) : FactoryItem<Packet>(rawId, factory)
    {
        public readonly bool ClientBound = clientBound;
        public readonly bool ServerBound = serverBound;
        public readonly bool WorldPacket = worldPacket;
    }

    private static PacketRegisterItem New(PacketId rawId, bool clientBound, bool serverBound, bool worldPacket, Func<Packet> factory) =>
        new((byte)rawId, clientBound, serverBound, worldPacket, factory);
}
