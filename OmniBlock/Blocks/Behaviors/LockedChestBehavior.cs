using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Vanilla Beta's unused locked-chest stub: renders as a facing-aware chest but
///     self-destructs the tick after it's placed (never had real lock functionality).
/// </summary>
internal sealed class LockedChestBehavior(int top, int side, int front) : BlockRuntimeBehavior, IBlockVisuals, IBlockTicker
{
    public void OnTick(Block block, OnTickEvent @event)
    {
        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
    }

    public int GetTexture(Block block, Side renderSide, int defaultTexture)
    {
        return renderSide switch
        {
            Side.Up or Side.Down => top,
            Side.South => front,
            _ => side
        };
    }

    public int GetTextureId(Block block, IBlockReader reader, int x, int y, int z, Side renderSide, int defaultTexture)
    {
        if (renderSide is Side.Up or Side.Down) return top;

        var blockNorth = reader.GetBlockId(x, y, z - 1);
        var blockSouth = reader.GetBlockId(x, y, z + 1);
        var blockWest = reader.GetBlockId(x - 1, y, z);
        var blockEast = reader.GetBlockId(x + 1, y, z);

        var facing = Side.South;
        if (Blocks.IsOpaque(blockNorth) && !Blocks.IsOpaque(blockSouth)) facing = Side.South;
        if (Blocks.IsOpaque(blockSouth) && !Blocks.IsOpaque(blockNorth)) facing = Side.North;
        if (Blocks.IsOpaque(blockWest) && !Blocks.IsOpaque(blockEast)) facing = Side.East;
        if (Blocks.IsOpaque(blockEast) && !Blocks.IsOpaque(blockWest)) facing = Side.West;

        return renderSide == facing ? front : side;
    }
}