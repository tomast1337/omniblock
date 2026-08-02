using BetaSharp.Network.Packets;

namespace BetaSharp.Tests.Packets;

public class PacketDataTest : PacketTestBase
{
    // Packets whose size depends on their contents. Every example that used to be here has
    // migrated to the message layer, where GeneratedMessageTests covers the same property
    // across every registered type rather than the few somebody remembered to list.

    [Theory, MemberData(nameof(PacketIds))]
    public void VerifyPacketDefaultReadWriteLenght(PacketId value)
    {
        Packet p = Packet.Get(value);

        MemoryStream stream = new();
        p.Write(stream);
        stream.Position = 0;
        p.Read(stream);

        Assert.StrictEqual(stream.Length, stream.Position);
        stream.Dispose();
    }
}
