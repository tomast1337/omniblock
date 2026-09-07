using OmniBlock.Registries;

namespace OmniBlock.Blocks;

public static class BlockRegistry
{
    /// <summary>Transitional client facade over the published immutable runtime.</summary>
    public static Block Get(string name) => ContentRuntime.Current.Blocks.Get(ResourceLocation.Parse(name));

    /// <summary>Returns the live block registered under the protocol-level numeric id.</summary>
    public static Block GetByProtocolId(int protocolId) => ContentRuntime.Current.Blocks.GetByProtocolId(protocolId);

    public static bool TryGetByProtocolId(int protocolId, out Block? block) =>
        ContentRuntime.Current.Blocks.TryGetByProtocolId(protocolId, out block);

    public static bool IsOpaque(int protocolId) => TryGetByProtocolId(protocolId, out var block) && block.IsOpaque;

    public static int GetOpacity(int protocolId) => TryGetByProtocolId(protocolId, out var block) ? block.Opacity : 0;

    public static int GetLightEmission(int protocolId) => TryGetByProtocolId(protocolId, out var block) ? block.LightEmission : 0;

    public static bool AllowsVision(int protocolId) => !TryGetByProtocolId(protocolId, out var block) || block.AllowsVision;

    public static bool HasBlockEntity(int protocolId) => TryGetByProtocolId(protocolId, out var block) && block.HasBlockEntity;

    public static bool TicksRandomly(int protocolId) => TryGetByProtocolId(protocolId, out var block) && block.TickRandomly;

    public static bool IgnoresMetaUpdates(int protocolId) => TryGetByProtocolId(protocolId, out var block) && block.IgnoreMetaUpdates;

}
