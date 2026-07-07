namespace BetaSharp.Blocks;

public interface IBlockTicker
{
    void OnTick(Block block, OnTickEvent @event) { }
    void RandomDisplayTick(Block block, OnTickEvent @event) { }

    /// <summary>
    /// Called when a neighboring block changes. The default implementation is a no-op —
    /// most blocks don't care about neighbors.
    /// </summary>
    void NeighborUpdate(Block block, OnTickEvent @event) { }
}
