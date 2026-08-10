using OmniBlock.Network.Packets;

namespace OmniBlock.Tests.Packets;

public abstract class PacketTestBase
{
    public static IEnumerable<object[]> PacketIds =>
        Enum.GetValues(typeof(PacketId))
            .Cast<PacketId>()
            .Select(v => new object[] { v });
}
