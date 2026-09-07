namespace OmniBlock.Blocks;

/// <summary>Protocol-ID view shared by blocks and their compiled behaviors.</summary>
public interface IBlockRuntimeView
{
    Block Get(ResourceLocation key);
    Block GetByProtocolId(int protocolId);
    bool TryGet(ResourceLocation key, out Block? block);
    bool TryGetByProtocolId(int protocolId, out Block? block);
}

public static class BlockRuntimeViewExtensions
{
    public static bool IsOpaque(this IBlockRuntimeView blocks, int protocolId) => blocks.TryGetByProtocolId(protocolId, out var block) && block.IsOpaque;

    public static int GetOpacity(this IBlockRuntimeView blocks, int protocolId) => blocks.TryGetByProtocolId(protocolId, out var block) ? block.Opacity : 0;

    public static int GetLightEmission(this IBlockRuntimeView blocks, int protocolId) => blocks.TryGetByProtocolId(protocolId, out var block) ? block.LightEmission : 0;
}

public sealed class StagedBlockRuntimeView : IBlockRuntimeView
{
    private readonly Dictionary<int, Block> _blocks = [];
    private readonly Dictionary<ResourceLocation, Block> _blocksByKey = [];
    private bool _frozen;

    public Block Get(ResourceLocation key)
    {
        return _blocksByKey.TryGetValue(key, out var block)
            ? block
            : throw new KeyNotFoundException($"Unknown block '{key}'.");
    }

    public Block GetByProtocolId(int protocolId)
    {
        return TryGetByProtocolId(protocolId, out var block)
            ? block
            : throw new KeyNotFoundException($"Unknown block protocol id {protocolId}.");
    }

    public bool TryGetByProtocolId(int protocolId, out Block? block) => _blocks.TryGetValue(protocolId, out block);

    public bool TryGet(ResourceLocation key, out Block? block) => _blocksByKey.TryGetValue(key, out block);

    internal void Add(ResourceLocation key, Block block)
    {
        if (_frozen) throw new InvalidOperationException("Block runtime view is finalized.");
        _blocks.TryAdd(block.Id, block);
        _blocksByKey.TryAdd(key, block);
    }

    internal void Freeze() => _frozen = true;
}
