namespace OmniBlock.Blocks.Behaviors;

/// <summary>Construction-time binding for behavior access to its owning block runtime.</summary>
public abstract class BlockRuntimeBehavior
{
    private IBlockRuntimeView? _blocks;

    protected IBlockRuntimeView Blocks => _blocks
                                          ?? throw new InvalidOperationException(
                                              $"{GetType().Name} was used before being bound to a block runtime.");

    internal void BindRuntime(IBlockRuntimeView blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        if (_blocks is not null && !ReferenceEquals(_blocks, blocks))
            throw new InvalidOperationException($"{GetType().Name} is already bound to another block runtime.");
        _blocks = blocks;
        OnRuntimeBound(blocks);
    }

    protected virtual void OnRuntimeBound(IBlockRuntimeView blocks)
    {
    }
}