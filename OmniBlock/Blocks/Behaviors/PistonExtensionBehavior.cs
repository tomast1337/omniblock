using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Piston head/extension: never independently placeable, breaks the base piston behind it when
///     destroyed, and forwards neighbor updates to the base piston it's still attached to.
/// </summary>
public sealed class PistonExtensionBehavior : IBlockPhysics, IBlockLifecycle, IBlockVisuals
{
    private const int PistonHeadSprite = -1;

    public void OnBreak(Block block, OnBreakEvent @event)
    {
        (int x, int y, int z) = (@event.X, @event.Y, @event.Z);
        int blockMeta = @event.World.Reader.GetBlockMeta(x, y, z);
        Side oppositeFace = GetFacing(blockMeta).OppositeFace();

        x += PistonConstants.HeadOffsetX[oppositeFace.ToInt()];
        y += PistonConstants.HeadOffsetY[oppositeFace.ToInt()];
        z += PistonConstants.HeadOffsetZ[oppositeFace.ToInt()];

        int blockId = @event.World.Reader.GetBlockId(x, y, z);
        if (blockId != BlockRegistry.Get("piston").Id && blockId != BlockRegistry.Get("sticky_piston").Id) return;

        int meta = @event.World.Reader.GetBlockMeta(x, y, z);
        if (!PistonBaseBehavior.IsExtended(meta)) return;

        BlockRegistry.GetByProtocolId(blockId).DropStacks(new OnDropEvent(@event.World, x, y, z, meta));
        @event.World.Writer.SetBlock(x, y, z, 0);
    }

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event) => false;

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        int meta = reader.GetBlockMeta(x, y, z);
        switch (GetFacing(meta))
        {
            case Side.Down:
                block.SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 0.25F, 1.0F);
                break;
            case Side.Up:
                block.SetBoundingBox(0.0F, 12.0F / 16.0F, 0.0F, 1.0F, 1.0F, 1.0F);
                break;
            case Side.North:
                block.SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 0.25F);
                break;
            case Side.South:
                block.SetBoundingBox(0.0F, 0.0F, 12.0F / 16.0F, 1.0F, 1.0F, 1.0F);
                break;
            case Side.West:
                block.SetBoundingBox(0.0F, 0.0F, 0.0F, 0.25F, 1.0F, 1.0F);
                break;
            case Side.East:
                block.SetBoundingBox(12.0F / 16.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
                break;
        }
    }

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        int facing = GetFacing(@event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)).ToInt();
        int blockId = @event.World.Reader.GetBlockId(@event.X - PistonConstants.HeadOffsetX[facing], @event.Y - PistonConstants.HeadOffsetY[facing], @event.Z - PistonConstants.HeadOffsetZ[facing]);
        if (blockId != BlockRegistry.Get("piston").Id && blockId != BlockRegistry.Get("sticky_piston").Id)
        {
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        }
        else
        {
            BlockRegistry.GetByProtocolId(blockId).NeighborUpdate(new OnTickEvent(@event.World, @event.X - PistonConstants.HeadOffsetX[facing], @event.Y - PistonConstants.HeadOffsetY[facing], @event.Z - PistonConstants.HeadOffsetZ[facing],
                @event.World.Reader.GetBlockMeta(@event.X - PistonConstants.HeadOffsetX[facing], @event.Y - PistonConstants.HeadOffsetY[facing], @event.Z - PistonConstants.HeadOffsetZ[facing]), block.Id));
        }
    }

    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
    {
        Side facing = GetFacing(meta);
        if (side == facing)
        {
            return (meta & 8) != 0 ? block.TextureId - 1 : block.TextureId;
        }

        return side == facing.OppositeFace() ? 107 : 108;
    }

    public static Side GetFacing(int meta) => (meta & 7).ToSide();
}
