namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Creates the block's tile entity (from its <c>setHasTileEntity</c> factory) on placement and
///     removes it on break. Shared by every flattened tile-entity block without extra break logic.
/// </summary>
public sealed class TileEntityLifecycleBehavior : IBlockLifecycle
{
    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (block.GetBlockEntity() is { } blockEntity)
        {
            @event.World.Entities.SetBlockEntity(@event.X, @event.Y, @event.Z, blockEntity);
        }
    }

    public void OnBreak(Block block, OnBreakEvent @event) => @event.World.Entities.RemoveBlockEntity(@event.X, @event.Y, @event.Z);
}
