namespace OmniBlock.Blocks;

/// <summary>Composable capability for block placement and destruction events.</summary>
public interface IBlockLifecycle
{
    void OnPlaced(Block block, OnPlacedEvent @event)
    {
    }

    void OnBreak(Block block, OnBreakEvent @event)
    {
    }

    void OnAfterBreak(Block block, OnAfterBreakEvent @event)
    {
    }

    /// <summary>Called when the block's metadata changes (e.g. client-side state sync).</summary>
    void OnMetadataChange(Block block, OnMetadataChangeEvent @event)
    {
    }

    /// <summary>Called when a block action network packet arrives (e.g. note played, piston moved).</summary>
    void OnBlockAction(Block block, OnBlockActionEvent @event)
    {
    }

    /// <summary>Overrides which item id this block drops for the given metadata.</summary>
    int GetDroppedItemId(Block block, int blockMeta, int defaultItemId) => defaultItemId;

    /// <summary>Overrides how many items this block drops (independent of the meta-driven id).</summary>
    int GetDroppedItemCount(Block block, int defaultCount) => defaultCount;

    /// <summary>Overrides the metadata/damage value stamped onto the dropped item stack.</summary>
    int GetDroppedItemMeta(Block block, int blockMeta, int defaultMeta) => defaultMeta;

    /// <summary>
    ///     Overrides middle-click "pick block": <c>primaryMeta</c> is the meta used to match/give the
    ///     block itself (default: the placed block's raw meta), and
    ///     <c>backupItemId</c>/<c>backupMeta</c> are what's given instead when the block itself isn't
    ///     obtainable (default: the block's loot table's primary drop). Leaves overrides both — the
    ///     placed block's meta carries decay/persistent flag bits alongside the wood-type variant, so
    ///     even matching/giving the raw leaves block needs those bits masked out.
    /// </summary>
    (int primaryMeta, int backupItemId, int backupMeta) GetPickBlockItem(Block block, int blockMeta, int defaultBackupId, int defaultBackupMeta) => (blockMeta, defaultBackupId, defaultBackupMeta);

    /// <summary>
    ///     Called once per block, after every block's static field has been assigned (see
    ///     <see cref="Block.Init" />), for setup that must reference other block statics regardless of
    ///     declaration order (e.g. fire's flammability registry).
    /// </summary>
    void OnInit(Block block)
    {
    }

    /// <summary>
    ///     Called after the default single-item drop resolution in <see cref="Block.DropStacks" />,
    ///     for blocks with bonus/variable drops beyond the id/count model (e.g. crops scattering
    ///     extra seeds).
    /// </summary>
    void OnDropStacks(Block block, OnDropEvent @event)
    {
    }

    /// <summary>Called when an explosion destroys this block, before it's removed from the world.</summary>
    void OnDestroyedByExplosion(Block block, OnDestroyedByExplosionEvent @event)
    {
    }
}
