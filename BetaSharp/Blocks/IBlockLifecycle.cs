namespace BetaSharp.Blocks;

/// <summary>Composable capability for block placement and destruction events.</summary>
public interface IBlockLifecycle
{
    void OnPlaced(Block block, OnPlacedEvent @event) { }
    void OnBreak(Block block, OnBreakEvent @event) { }
    void OnAfterBreak(Block block, OnAfterBreakEvent @event) { }
}
