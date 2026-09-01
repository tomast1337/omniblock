namespace OmniBlock.Blocks;

/// <summary>Protocol-ID view shared by blocks and their compiled behaviors.</summary>
public interface IBlockRuntimeView
{
    Block Get(ResourceLocation key);
    Block GetByProtocolId(int protocolId);
    bool TryGetByProtocolId(int protocolId, out Block? block);
}

public static class BlockRuntimeViewExtensions
{
    public static bool IsOpaque(this IBlockRuntimeView blocks, int protocolId) =>
        blocks.TryGetByProtocolId(protocolId, out Block? block) && block.IsOpaque;

    public static int GetOpacity(this IBlockRuntimeView blocks, int protocolId) =>
        blocks.TryGetByProtocolId(protocolId, out Block? block) ? block.Opacity : 0;

    public static int GetLightEmission(this IBlockRuntimeView blocks, int protocolId) =>
        blocks.TryGetByProtocolId(protocolId, out Block? block) ? block.LightEmission : 0;
}

public sealed class StagedBlockRuntimeView : IBlockRuntimeView
{
    private readonly Dictionary<int, Block> _blocks = [];
    private readonly Dictionary<ResourceLocation, Block> _blocksByKey = [];
    private bool _frozen;

    internal void Add(ResourceLocation key, Block block)
    {
        if (_frozen) throw new InvalidOperationException("Block runtime view is finalized.");
        _blocks.TryAdd(block.Id, block);
        _blocksByKey.TryAdd(key, block);
    }

    internal void Freeze() => _frozen = true;

    public Block Get(ResourceLocation key) =>
        _blocksByKey.TryGetValue(key, out Block? block)
            ? block
            : throw new KeyNotFoundException($"Unknown block '{key}'.");

    public Block GetByProtocolId(int protocolId) =>
        TryGetByProtocolId(protocolId, out Block? block)
            ? block
            : throw new KeyNotFoundException($"Unknown block protocol id {protocolId}.");

    public bool TryGetByProtocolId(int protocolId, out Block? block)
    {
        return _blocks.TryGetValue(protocolId, out block);
    }
}
