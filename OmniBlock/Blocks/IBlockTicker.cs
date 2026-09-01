namespace OmniBlock.Blocks;

public interface IBlockTicker
{
    void OnTick(Block block, OnTickEvent @event)
    {
    }

    void RandomDisplayTick(Block block, OnTickEvent @event)
    {
    }
}