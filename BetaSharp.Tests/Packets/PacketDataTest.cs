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

    /// <summary>
    ///     Every remaining packet reports the byte count it actually writes.
    ///     <para>
    ///         The check the message layer gets from its generator, which cannot produce a
    ///         <c>Size()</c> that disagrees with its <c>Write()</c> because one table emits both.
    ///         These four are hand-written and outside that guarantee, and the round-trip above does
    ///         not catch a wrong size — it only proves the reader consumes what the writer produced,
    ///         which stays true however far off <c>Size()</c> is.
    ///     </para>
    /// </summary>
    [Theory, MemberData(nameof(PacketIds))]
    public void VerifyPacketReportsTheSizeItWrites(PacketId value)
    {
        Packet p = Packet.Get(value);

        using MemoryStream stream = new();
        p.Write(stream);

        Assert.Equal(stream.Length, p.Size());
    }
}
