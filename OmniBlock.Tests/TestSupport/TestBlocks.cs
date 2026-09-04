using OmniBlock.Blocks;
using OmniBlock.Registries;

namespace OmniBlock.Tests.TestSupport;

/// <summary>
/// Test-only block lookups that bypass the transitional <see cref="BlockRegistry" /> static facade.
/// The published runtime is the same object BlockRegistry used to forward to; keeping this wrapper
/// means tests do not create new production dependencies on the static registry.
/// </summary>
public static class TestBlocks
{
    public static Block Get(string name) => ContentRuntime.Current.Blocks.Get(name);
    public static Block GetByProtocolId(int protocolId) => ContentRuntime.Current.Blocks.GetByProtocolId(protocolId);
    public static bool TryGetByProtocolId(int protocolId, out Block? block) => ContentRuntime.Current.Blocks.TryGetByProtocolId(protocolId, out block);

    public static bool IsOpaque(int protocolId) => ContentRuntime.Current.Blocks.IsOpaque(protocolId);
    public static int GetOpacity(int protocolId) => ContentRuntime.Current.Blocks.GetOpacity(protocolId);
    public static int GetLightEmission(int protocolId) => ContentRuntime.Current.Blocks.GetLightEmission(protocolId);

    public static bool AllowsVision(int protocolId) =>
        !ContentRuntime.Current.Blocks.TryGetByProtocolId(protocolId, out Block? block) || block?.AllowsVision == true;

    public static bool HasBlockEntity(int protocolId) =>
        ContentRuntime.Current.Blocks.TryGetByProtocolId(protocolId, out Block? block) && block?.HasBlockEntity == true;

    public static bool TicksRandomly(int protocolId) =>
        ContentRuntime.Current.Blocks.TryGetByProtocolId(protocolId, out Block? block) && block?.TickRandomly == true;

    public static bool IgnoresMetaUpdates(int protocolId) =>
        ContentRuntime.Current.Blocks.TryGetByProtocolId(protocolId, out Block? block) && block?.IgnoreMetaUpdates == true;
}
