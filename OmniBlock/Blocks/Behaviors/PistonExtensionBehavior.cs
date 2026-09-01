using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Piston head/extension: never independently placeable, breaks the base piston behind it when
///     destroyed, and forwards neighbor updates to the base piston it's still attached to.
/// </summary>
public sealed class PistonExtensionBehavior : BlockRuntimeBehavior, IBlockPhysics, IBlockLifecycle, IBlockVisuals
{
    private const int PistonHeadSprite = -1;

    public void OnBreak(Block block, OnBreakEvent @event)
    {
        var (x, y, z) = (@event.X, @event.Y, @event.Z);
        var blockMeta = @event.World.Reader.GetBlockMeta(x, y, z);
        var oppositeFace = GetFacing(blockMeta).OppositeFace();

        x += PistonConstants.HeadOffsetX[oppositeFace.ToInt()];
        y += PistonConstants.HeadOffsetY[oppositeFace.ToInt()];
        z += PistonConstants.HeadOffsetZ[oppositeFace.ToInt()];

        var blockId = @event.World.Reader.GetBlockId(x, y, z);
        if (blockId != Blocks.Get("piston").Id && blockId != Blocks.Get("sticky_piston").Id) return;

        var meta = @event.World.Reader.GetBlockMeta(x, y, z);
        if (!PistonBaseBehavior.IsExtended(meta)) return;

        Blocks.GetByProtocolId(blockId).DropStacks(new OnDropEvent(@event.World, x, y, z, meta));
        @event.World.Writer.SetBlock(x, y, z, 0);
    }

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event)
    {
        return false;
    }

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        var meta = reader.GetBlockMeta(x, y, z);
        switch (GetFacing(meta))
        {
            case Side.Down:
                block.SetRuntimeBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 0.25F, 1.0F);
                break;
            case Side.Up:
                block.SetRuntimeBoundingBox(0.0F, 12.0F / 16.0F, 0.0F, 1.0F, 1.0F, 1.0F);
                break;
            case Side.North:
                block.SetRuntimeBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 0.25F);
                break;
            case Side.South:
                block.SetRuntimeBoundingBox(0.0F, 0.0F, 12.0F / 16.0F, 1.0F, 1.0F, 1.0F);
                break;
            case Side.West:
                block.SetRuntimeBoundingBox(0.0F, 0.0F, 0.0F, 0.25F, 1.0F, 1.0F);
                break;
            case Side.East:
                block.SetRuntimeBoundingBox(12.0F / 16.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
                break;
        }
    }

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        var facing = GetFacing(@event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)).ToInt();
        var blockId = @event.World.Reader.GetBlockId(@event.X - PistonConstants.HeadOffsetX[facing], @event.Y - PistonConstants.HeadOffsetY[facing], @event.Z - PistonConstants.HeadOffsetZ[facing]);
        if (blockId != Blocks.Get("piston").Id && blockId != Blocks.Get("sticky_piston").Id)
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        else
            Blocks.GetByProtocolId(blockId).NeighborUpdate(new OnTickEvent(@event.World, @event.X - PistonConstants.HeadOffsetX[facing], @event.Y - PistonConstants.HeadOffsetY[facing], @event.Z - PistonConstants.HeadOffsetZ[facing],
                @event.World.Reader.GetBlockMeta(@event.X - PistonConstants.HeadOffsetX[facing], @event.Y - PistonConstants.HeadOffsetY[facing], @event.Z - PistonConstants.HeadOffsetZ[facing]), block.Id));
    }

    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
    {
        var facing = GetFacing(meta);
        if (side == facing) return (meta & 8) != 0 ? block.TextureId - 1 : block.TextureId;

        return side == facing.OppositeFace() ? 107 : 108;
    }

    public static Side GetFacing(int meta)
    {
        return (meta & 7).ToSide();
    }
}