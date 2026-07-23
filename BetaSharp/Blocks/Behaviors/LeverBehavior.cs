using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Lever: use toggles metadata bit 8 and holds power until toggled back. Supports wall and
///     floor mounting. Assign to the Redstone, Interactable, Physics, and Lifecycle slots.
/// </summary>
public sealed class LeverBehavior : IRedstoneComponent, IBlockInteractable, IBlockPhysics, IBlockLifecycle
{
    public void OnBlockBreakStart(Block block, OnBlockBreakStartEvent @event) => ToggleLever(block, @event.World, @event.X, @event.Y, @event.Z);

    public bool OnUse(Block block, OnUseEvent @event)
    {
        if (@event.World.IsRemote) return true;

        ToggleLever(block, @event.World, @event.X, @event.Y, @event.Z);
        return true;
    }

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        int powered = meta & 8;
        meta = -1;

        switch (@event.Direction)
        {
            case Side.Up when @event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z):
                meta = 5 + Random.Shared.Next(2);
                break;
            case Side.North when @event.World.Reader.ShouldSuffocate(@event.X, @event.Y, @event.Z + 1):
                meta = 4;
                break;
            case Side.South when @event.World.Reader.ShouldSuffocate(@event.X, @event.Y, @event.Z - 1):
                meta = 3;
                break;
            case Side.West when @event.World.Reader.ShouldSuffocate(@event.X + 1, @event.Y, @event.Z):
                meta = 2;
                break;
            case Side.East when @event.World.Reader.ShouldSuffocate(@event.X - 1, @event.Y, @event.Z):
                meta = 1;
                break;
            default:
                {
                    if (@event.World.Reader.ShouldSuffocate(@event.X - 1, @event.Y, @event.Z))
                    {
                        meta = 1;
                    }
                    else if (@event.World.Reader.ShouldSuffocate(@event.X + 1, @event.Y, @event.Z))
                    {
                        meta = 2;
                    }
                    else if (@event.World.Reader.ShouldSuffocate(@event.X, @event.Y, @event.Z - 1))
                    {
                        meta = 3;
                    }
                    else if (@event.World.Reader.ShouldSuffocate(@event.X, @event.Y, @event.Z + 1))
                    {
                        meta = 4;
                    }
                    else if (@event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z))
                    {
                        meta = 5 + Random.Shared.Next(2);
                    }

                    break;
                }
        }

        if (meta == -1)
        {
            block.DropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)));
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        }
        else
        {
            @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, meta + powered);
        }
    }

    public void OnBreak(Block block, OnBreakEvent ctx)
    {
        int meta = ctx.World.Reader.GetBlockMeta(ctx.X, ctx.Y, ctx.Z);
        if ((meta & 8) <= 0) return;

        ctx.World.Broadcaster.NotifyNeighbors(ctx.X, ctx.Y, ctx.Z, block.id);
        int direction = meta & 7;

        switch (direction)
        {
            case 1:
                ctx.World.Broadcaster.NotifyNeighbors(ctx.X - 1, ctx.Y, ctx.Z, block.id);
                break;
            case 2:
                ctx.World.Broadcaster.NotifyNeighbors(ctx.X + 1, ctx.Y, ctx.Z, block.id);
                break;
            case 3:
                ctx.World.Broadcaster.NotifyNeighbors(ctx.X, ctx.Y, ctx.Z - 1, block.id);
                break;
            case 4:
                ctx.World.Broadcaster.NotifyNeighbors(ctx.X, ctx.Y, ctx.Z + 1, block.id);
                break;
            default:
                ctx.World.Broadcaster.NotifyNeighbors(ctx.X, ctx.Y - 1, ctx.Z, block.id);
                break;
        }
    }

    public bool CanPlaceAt(Block block, CanPlaceAtContext context) => HasSupport(context.World.Reader, context.X, context.Y, context.Z);

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (!BreakIfCannotPlaceAt(block, @event)) return;

        int direction = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z) & 7;

        bool shouldDrop = (!@event.World.Reader.ShouldSuffocate(@event.X - 1, @event.Y, @event.Z) && direction == 1) ||
                          (!@event.World.Reader.ShouldSuffocate(@event.X + 1, @event.Y, @event.Z) && direction == 2) ||
                          (!@event.World.Reader.ShouldSuffocate(@event.X, @event.Y, @event.Z - 1) && direction == 3) ||
                          (!@event.World.Reader.ShouldSuffocate(@event.X, @event.Y, @event.Z + 1) && direction == 4) ||
                          (!@event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z) && direction == 5) ||
                          (!@event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z) && direction == 6);

        if (!shouldDrop) return;

        block.DropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)));
        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
    }

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        int meta = reader.GetBlockMeta(x, y, z) & 7;
        float width = 3.0F / 16.0F;

        switch (meta)
        {
            case 1:
                block.SetBoundingBox(0.0F, 0.2F, 0.5F - width, width * 2.0F, 0.8F, 0.5F + width);
                break;
            case 2:
                block.SetBoundingBox(1.0F - width * 2.0F, 0.2F, 0.5F - width, 1.0F, 0.8F, 0.5F + width);
                break;
            case 3:
                block.SetBoundingBox(0.5F - width, 0.2F, 0.0F, 0.5F + width, 0.8F, width * 2.0F);
                break;
            case 4:
                block.SetBoundingBox(0.5F - width, 0.2F, 1.0F - width * 2.0F, 0.5F + width, 0.8F, 1.0F);
                break;
            default:
                width = 0.25F;
                block.SetBoundingBox(0.5F - width, 0.0F, 0.5F - width, 0.5F + width, 0.6F, 0.5F + width);
                break;
        }
    }

    public bool IsPoweringSide(Block block, IBlockReader reader, int x, int y, int z, int side) =>
        (reader.GetBlockMeta(x, y, z) & 8) > 0;

    public bool IsStrongPoweringSide(Block block, IBlockReader world, int x, int y, int z, int side)
    {
        int meta = world.GetBlockMeta(x, y, z);
        if ((meta & 8) == 0) return false;

        int direction = meta & 7;
        return (direction == 6 && side == 1) ||
               (direction == 5 && side == 1) ||
               (direction == 4 && side == 2) ||
               (direction == 3 && side == 3) ||
               (direction == 2 && side == 4) ||
               (direction == 1 && side == 5);
    }

    public bool CanEmitRedstonePower(Block block) => true;

    private static bool HasSupport(IBlockReader reader, int x, int y, int z) =>
        reader.ShouldSuffocate(x - 1, y, z) ||
        reader.ShouldSuffocate(x + 1, y, z) ||
        reader.ShouldSuffocate(x, y, z - 1) ||
        reader.ShouldSuffocate(x, y, z + 1) ||
        reader.ShouldSuffocate(x, y - 1, z);

    private static bool BreakIfCannotPlaceAt(Block block, OnTickEvent ctx)
    {
        // Direct support check — the composed Block.canPlaceAt also tests replaceability of the
        // lever's own occupied position and would always fail here.
        if (HasSupport(ctx.World.Reader, ctx.X, ctx.Y, ctx.Z)) return true;

        block.DropStacks(new OnDropEvent(ctx.World, ctx.X, ctx.Y, ctx.Z, ctx.World.Reader.GetBlockMeta(ctx.X, ctx.Y, ctx.Z)));
        ctx.World.Writer.SetBlock(ctx.X, ctx.Y, ctx.Z, 0);
        return false;
    }

    private static void ToggleLever(Block block, IWorldContext world, int x, int y, int z)
    {
        int meta = world.Reader.GetBlockMeta(x, y, z);
        int direction = meta & 7;
        int powered = 8 - (meta & 8);

        world.Writer.SetBlockMeta(x, y, z, direction + powered);
        world.Broadcaster.SetBlocksDirty(x, y, z);
        world.Broadcaster.PlaySoundAtPos(x + 0.5D, y + 0.5D, z + 0.5D, "random.click", 0.3F, powered > 0 ? 0.6F : 0.5F);
        world.Broadcaster.NotifyNeighbors(x, y, z, block.id);

        switch (direction)
        {
            case 1:
                world.Broadcaster.NotifyNeighbors(x - 1, y, z, block.id);
                break;
            case 2:
                world.Broadcaster.NotifyNeighbors(x + 1, y, z, block.id);
                break;
            case 3:
                world.Broadcaster.NotifyNeighbors(x, y, z - 1, block.id);
                break;
            case 4:
                world.Broadcaster.NotifyNeighbors(x, y, z + 1, block.id);
                break;
            default:
                world.Broadcaster.NotifyNeighbors(x, y - 1, z, block.id);
                break;
        }
    }
}
