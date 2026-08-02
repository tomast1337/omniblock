using BetaSharp.Network.Packets;
using BetaSharp.Network.Packets.Play;
using BetaSharp.Network.Packets.S2CPlay;
using BetaSharp.Util.Maths;

namespace BetaSharp.Tests.Packets;

public class PacketDataTest : PacketTestBase
{
    // Packets whose size depends on their contents. The inventory and slot examples that used to
    // be here have migrated to the message layer, where GeneratedMessageTests covers the same
    // property across every registered type rather than the few somebody remembered to list.
    public static IEnumerable<object[]> ExamplePackets = new List<Packet[]>
    {
        new Packet[] { ChatMessagePacket.Get("a message long enough to be worth measuring") },
        new Packet[] { MapUpdateS2CPacket.Get(358, 0, [1, 2, 3, 4, 5, 6, 7, 8]) },
        new Packet[] { ExplosionS2CPacket.Get(0.5, 64.0, -0.5, 3.0F, [new BlockPos(0, 64, 0), new BlockPos(1, 64, 0)]) },
    };

    [Theory, MemberData(nameof(PacketIds))]
    public void VerifyPacketDefaultReadWriteLenght(PacketId value)
    {
        Packet p;
        if (value == PacketId.RegistryDataS2C)
        {
            // special case for RegistryDataS2CPacket
            p = RegistryDataS2CPacket.Get(new ResourceLocation(Namespace.BetaSharp, "test"), []);
        }
        else
        {
            p = Packet.Get(value);
        }

        MemoryStream stream = new();
        p.Write(stream);
        stream.Position = 0;
        p.Read(stream);

        Assert.StrictEqual(stream.Length, stream.Position);
        stream.Dispose();
    }

    [Theory, MemberData(nameof(ExamplePackets))]
    public void VerifyPacketReadWriteLenght(Packet value)
    {
        MemoryStream stream = new();
        value.Write(stream);
        stream.Position = 0;
        value.Read(stream);

        Assert.StrictEqual(stream.Length, stream.Position);
        stream.Dispose();
    }
}
