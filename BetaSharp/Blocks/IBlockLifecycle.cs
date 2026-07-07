namespace BetaSharp.Blocks;

/// <summary>Composable capability for block placement and destruction events.</summary>
public interface IBlockLifecycle
{
    void OnPlaced(Block block, OnPlacedEvent @event) { }
    void OnBreak(Block block, OnBreakEvent @event) { }
    void OnAfterBreak(Block block, OnAfterBreakEvent @event) { }

    /// <summary>Called when the block's metadata changes (e.g. client-side state sync).</summary>
    void OnMetadataChange(Block block, OnMetadataChangeEvent @event) { }

    /// <summary>Called when a block action network packet arrives (e.g. note played, piston moved).</summary>
    void OnBlockAction(Block block, OnBlockActionEvent @event) { }

    /// <summary>Overrides which item id this block drops for the given metadata.</summary>
    int GetDroppedItemId(Block block, int blockMeta, int defaultItemId) => defaultItemId;

    /// <summary>Overrides how many items this block drops (independent of the meta-driven id).</summary>
    int GetDroppedItemCount(Block block, int defaultCount) => defaultCount;

    /// <summary>
    /// Called after the default single-item drop resolution in <see cref="Block.dropStacks"/>,
    /// for blocks with bonus/variable drops beyond the id/count model (e.g. crops scattering
    /// extra seeds).
    /// </summary>
    void OnDropStacks(Block block, OnDropEvent @event) { }
}
