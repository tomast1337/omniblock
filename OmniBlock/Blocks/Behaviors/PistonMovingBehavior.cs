using OmniBlock.Blocks.Entities;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Transient placeholder block rendered/collided in place of whatever's mid-slide behind a
///     piston; all its shape and behavior is borrowed from the <see cref="BlockEntityPiston" /> sitting
///     on the same tile, interpolated by push progress. Not independently placeable.
/// </summary>
public sealed class PistonMovingBehavior : BlockRuntimeBehavior, IBlockPhysics, IBlockLifecycle, IBlockInteractable
{
    public bool OnUse(Block block, OnUseEvent @event)
    {
        if (@event.World.IsRemote || @event.World.Entities.PeekBlockEntity<BlockEntity>(@event.X, @event.Y, @event.Z) != null) return false;

        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        return true;
    }

    public void OnBreak(Block block, OnBreakEvent @event)
    {
        var entity = @event.World.Entities.PeekBlockEntity<BlockEntity>(@event.X, @event.Y, @event.Z);
        if (entity is BlockEntityPiston piston)
            piston.Finish();
        else
            @event.World.Entities.RemoveBlockEntity(@event.X, @event.Y, @event.Z);
    }

    public int GetDroppedItemId(Block block, int blockMeta, int defaultItemId)
    {
        return 0;
    }

    public void OnDropStacks(Block block, OnDropEvent @event)
    {
        if (@event.World.IsRemote) return;

        var piston = @event.World.Entities.GetBlockEntity<BlockEntityPiston>(@event.X, @event.Y, @event.Z);
        if (piston != null) Blocks.GetByProtocolId(piston.PushedBlockId).DropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, piston.PushedBlockData));
    }

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event)
    {
        return false;
    }

    public Box? GetCollisionShape(Block block, IBlockReader reader, EntityManager entities, int x, int y, int z, Box? defaultShape)
    {
        var piston = entities.GetBlockEntity<BlockEntityPiston>(x, y, z);
        if (piston == null) return null;

        var progress = piston.GetProgress(0.0F);
        if (piston.IsExtending) progress = 1.0F - progress;

        return GetPushedBlockCollisionShape(block, reader, entities, x, y, z, piston.PushedBlockId, progress, piston.Facing);
    }

    public void UpdateBoundingBox(Block block, IBlockReader reader, EntityManager? entities, int x, int y, int z)
    {
        var piston = entities?.GetBlockEntity<BlockEntityPiston>(x, y, z);
        if (piston == null) return;

        var pushed = Blocks.GetByProtocolId(piston.PushedBlockId);
        if (pushed == block) return;

        pushed.UpdateBoundingBox(reader, entities, x, y, z);
        var progress = piston.GetProgress(0.0F);
        if (piston.IsExtending) progress = 1.0F - progress;

        var facing = piston.Facing;
        block.SetRuntimeBoundingBox(block.BoundingBox.Offset(
            -(double)(PistonConstants.HeadOffsetX[facing] * progress),
            -(double)(PistonConstants.HeadOffsetY[facing] * progress),
            -(double)(PistonConstants.HeadOffsetZ[facing] * progress)));
    }

    public static BlockEntity CreatePistonBlockEntity(int blockId, int blockMeta, int facing, bool extending, bool source)
    {
        return new BlockEntityPiston(blockId, blockMeta, facing, extending, source);
    }

    public Box? GetPushedBlockCollisionShape(Block block, IBlockReader world, EntityManager entities, int x, int y, int z, int blockId, float sizeMultiplier, int facing)
    {
        if (blockId == 0 || blockId == block.Id) return null;

        var shape = Blocks.GetByProtocolId(blockId).GetCollisionShape(world, entities, x, y, z);
        if (shape == null) return null;

        var res = shape.Value;
        res.MinX -= PistonConstants.HeadOffsetX[facing] * sizeMultiplier;
        res.MaxX -= PistonConstants.HeadOffsetX[facing] * sizeMultiplier;
        res.MinY -= PistonConstants.HeadOffsetY[facing] * sizeMultiplier;
        res.MaxY -= PistonConstants.HeadOffsetY[facing] * sizeMultiplier;
        res.MinZ -= PistonConstants.HeadOffsetZ[facing] * sizeMultiplier;
        res.MaxZ -= PistonConstants.HeadOffsetZ[facing] * sizeMultiplier;
        return res;
    }
}