using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>Vanilla Beta's unused locked-chest stub: renders as a facing-aware chest but
/// self-destructs the tick after it's placed (never had real lock functionality).</summary>
internal sealed class LockedChestBehavior : IBlockVisuals, IBlockTicker
{
    public int GetTexture(Block block, Side side, int defaultTexture) => side switch
    {
        Side.Up or Side.Down => BlockTextures.ChestTopBottom,
        Side.South => BlockTextures.ChestSingleFront,
        _ => BlockTextures.ChestSingleSide
    };

    public int GetTextureId(Block block, IBlockReader reader, int x, int y, int z, Side side, int defaultTexture)
    {
        if (side is Side.Up or Side.Down) return BlockTextures.ChestTopBottom;

        int blockNorth = reader.GetBlockId(x, y, z - 1);
        int blockSouth = reader.GetBlockId(x, y, z + 1);
        int blockWest = reader.GetBlockId(x - 1, y, z);
        int blockEast = reader.GetBlockId(x + 1, y, z);

        Side facing = Side.South;
        if (Block.BlocksOpaque[blockNorth] && !Block.BlocksOpaque[blockSouth]) facing = Side.South;
        if (Block.BlocksOpaque[blockSouth] && !Block.BlocksOpaque[blockNorth]) facing = Side.North;
        if (Block.BlocksOpaque[blockWest] && !Block.BlocksOpaque[blockEast]) facing = Side.East;
        if (Block.BlocksOpaque[blockEast] && !Block.BlocksOpaque[blockWest]) facing = Side.West;

        return side == facing ? BlockTextures.ChestSingleFront : BlockTextures.ChestSingleSide;
    }

    public void OnTick(Block block, OnTickEvent @event) => @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
}
