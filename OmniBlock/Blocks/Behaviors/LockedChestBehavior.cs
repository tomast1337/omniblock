using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>Vanilla Beta's unused locked-chest stub: renders as a facing-aware chest but
/// self-destructs the tick after it's placed (never had real lock functionality).</summary>
internal sealed class LockedChestBehavior(int top, int side, int front) : IBlockVisuals, IBlockTicker
{
    public int GetTexture(Block block, Side renderSide, int defaultTexture) => renderSide switch
    {
        Side.Up or Side.Down => top,
        Side.South => front,
        _ => side
    };

    public int GetTextureId(Block block, IBlockReader reader, int x, int y, int z, Side renderSide, int defaultTexture)
    {
        if (renderSide is Side.Up or Side.Down) return top;

        int blockNorth = reader.GetBlockId(x, y, z - 1);
        int blockSouth = reader.GetBlockId(x, y, z + 1);
        int blockWest = reader.GetBlockId(x - 1, y, z);
        int blockEast = reader.GetBlockId(x + 1, y, z);

        Side facing = Side.South;
        if (Block.BlocksOpaque[blockNorth] && !Block.BlocksOpaque[blockSouth]) facing = Side.South;
        if (Block.BlocksOpaque[blockSouth] && !Block.BlocksOpaque[blockNorth]) facing = Side.North;
        if (Block.BlocksOpaque[blockWest] && !Block.BlocksOpaque[blockEast]) facing = Side.East;
        if (Block.BlocksOpaque[blockEast] && !Block.BlocksOpaque[blockWest]) facing = Side.West;

        return renderSide == facing ? front : side;
    }

    public void OnTick(Block block, OnTickEvent @event) => @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
}
