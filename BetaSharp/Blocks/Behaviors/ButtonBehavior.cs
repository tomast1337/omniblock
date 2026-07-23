using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Stone button: press toggles metadata bit 8, emits power while pressed, and un-presses
///     after <c>getTickRate()</c> ticks. Assign to the Redstone, Interactable, Ticker, Physics,
///     and Lifecycle slots.
/// </summary>
public sealed class ButtonBehavior : IRedstoneComponent, IBlockInteractable, IBlockTicker, IBlockPhysics, IBlockLifecycle
{
    private const float MinY = 6.0F / 16.0F;
    private const float MaxY = 10.0F / 16.0F;
    private const float HalfWidth = 3.0F / 16.0F;
    private const float Thickness = 2.0F / 16.0F;
    private const float PressedThickness = 1.0F / 16.0F;

    public void OnBlockBreakStart(Block block, OnBlockBreakStartEvent @event) => UpdateState(block, @event.World, @event.X, @event.Y, @event.Z);

    public bool OnUse(Block block, OnUseEvent @event) => UpdateState(block, @event.World, @event.X, @event.Y, @event.Z);

    public void OnPlaced(Block block, OnPlacedEvent evt)
    {
        int facing = evt.World.Reader.GetBlockMeta(evt.X, evt.Y, evt.Z);
        int pressedBit = facing & 8;
        facing = evt.Direction switch
        {
            Side.North when evt.World.Reader.ShouldSuffocate(evt.X, evt.Y, evt.Z + 1) => 4,
            Side.South when evt.World.Reader.ShouldSuffocate(evt.X, evt.Y, evt.Z - 1) => 3,
            Side.West when evt.World.Reader.ShouldSuffocate(evt.X + 1, evt.Y, evt.Z) => 2,
            Side.East when evt.World.Reader.ShouldSuffocate(evt.X - 1, evt.Y, evt.Z) => 1,
            _ => GetPlacementSide(evt.World.Reader, evt.X, evt.Y, evt.Z)
        };

        evt.World.Writer.SetBlockMeta(evt.X, evt.Y, evt.Z, facing + pressedBit);
    }

    public void OnBreak(Block block, OnBreakEvent @event)
    {
        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        if ((meta & 8) <= 0) return;

        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y, @event.Z, block.id);
        int facing = meta & 7;
        switch (facing)
        {
            case 1:
                @event.World.Broadcaster.NotifyNeighbors(@event.X - 1, @event.Y, @event.Z, block.id);
                break;
            case 2:
                @event.World.Broadcaster.NotifyNeighbors(@event.X + 1, @event.Y, @event.Z, block.id);
                break;
            case 3:
                @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y, @event.Z - 1, block.id);
                break;
            case 4:
                @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y, @event.Z + 1, block.id);
                break;
            default:
                @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y - 1, @event.Z, block.id);
                break;
        }
    }

    public bool CanPlaceAt(Block block, CanPlaceAtContext context) => IsValidPlacementSide(context.World.Reader, context.X, context.Y, context.Z, context.Direction);

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (!BreakIfCannotPlaceAt(block, @event)) return;

        int facing = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z) & 7;
        bool shouldBreak = (!@event.World.Reader.ShouldSuffocate(@event.X - 1, @event.Y, @event.Z) && facing == 1) ||
                           (!@event.World.Reader.ShouldSuffocate(@event.X + 1, @event.Y, @event.Z) && facing == 2) ||
                           (!@event.World.Reader.ShouldSuffocate(@event.X, @event.Y, @event.Z - 1) && facing == 3) ||
                           (!@event.World.Reader.ShouldSuffocate(@event.X, @event.Y, @event.Z + 1) && facing == 4);

        if (!shouldBreak) return;

        block.DropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)));
        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
    }

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        int meta = reader.GetBlockMeta(x, y, z);
        Side facing = (meta & 7).ToSide();
        bool isPressed = (meta & 8) > 0;

        float height = Thickness;
        if (isPressed)
        {
            height = PressedThickness;
        }

        switch (facing)
        {
            case Side.Up:
                block.SetBoundingBox(0.0F, MinY, 0.5F - HalfWidth, height, MaxY, 0.5F + HalfWidth);
                break;
            case Side.North:
                block.SetBoundingBox(1.0F - height, MinY, 0.5F - HalfWidth, 1.0F, MaxY, 0.5F + HalfWidth);
                break;
            case Side.South:
                block.SetBoundingBox(0.5F - HalfWidth, MinY, 0.0F, 0.5F + HalfWidth, MaxY, height);
                break;
            case Side.West:
                block.SetBoundingBox(0.5F - HalfWidth, MinY, 1.0F - height, 0.5F + HalfWidth, MaxY, 1.0F);
                break;
        }
    }

    public void SetupRenderBoundingBox(Block block) =>
        block.SetBoundingBox(0.5F - HalfWidth, 0.5F - Thickness, 0.5F - Thickness, 0.5F + HalfWidth, 0.5F + Thickness, 0.5F + Thickness);

    public void OnTick(Block block, OnTickEvent @event)
    {
        if (@event.World.IsRemote) return;

        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        if ((meta & 8) == 0) return;
        @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, meta & 7);
        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y, @event.Z, block.id);
        int facing = meta & 7;
        switch (facing)
        {
            case 1:
                @event.World.Broadcaster.NotifyNeighbors(@event.X - 1, @event.Y, @event.Z, block.id);
                break;
            case 2:
                @event.World.Broadcaster.NotifyNeighbors(@event.X + 1, @event.Y, @event.Z, block.id);
                break;
            case 3:
                @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y, @event.Z - 1, block.id);
                break;
            case 4:
                @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y, @event.Z + 1, block.id);
                break;
            default:
                @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y - 1, @event.Z, block.id);
                break;
        }

        @event.World.Broadcaster.PlaySoundAtPos(@event.X + 0.5D, @event.Y + 0.5D, @event.Z + 0.5D, "random.click", 0.3F, 0.5F);
        @event.World.Broadcaster.SetBlocksDirty(@event.X, @event.Y, @event.Z);
    }

    public bool IsPoweringSide(Block block, IBlockReader reader, int x, int y, int z, int side) => (reader.GetBlockMeta(x, y, z) & 8) > 0;

    public bool IsStrongPoweringSide(Block block, IBlockReader read, int x, int y, int z, int side)
    {
        int meta = read.GetBlockMeta(x, y, z);
        if ((meta & 8) == 0) return false;

        int facing = meta & 7;
        return (facing == 5 && side == 1) ||
               (facing == 4 && side == 2) ||
               (facing == 3 && side == 3) ||
               (facing == 2 && side == 4) ||
               (facing == 1 && side == 5);
    }

    public bool CanEmitRedstonePower(Block block) => true;

    private static bool IsValidPlacementSide(IBlockReader read, int x, int y, int z, Side side = Side.Down)
    {
        if (side == Side.North) return read.ShouldSuffocate(x, y, z + 1);

        return read.ShouldSuffocate(x - 1, y, z) ||
               read.ShouldSuffocate(x + 1, y, z) ||
               read.ShouldSuffocate(x, y, z - 1) ||
               read.ShouldSuffocate(x, y, z + 1);
    }

    private static int GetPlacementSide(IBlockReader world, int x, int y, int z) =>
        world.ShouldSuffocate(x - 1, y, z) ? 1 : world.ShouldSuffocate(x + 1, y, z) ? 2 : world.ShouldSuffocate(x, y, z - 1) ? 3 : world.ShouldSuffocate(x, y, z + 1) ? 4 : 1;

    private static bool BreakIfCannotPlaceAt(Block block, OnTickEvent @event)
    {
        // Direct support check — the composed Block.canPlaceAt also tests replaceability of the
        // button's own occupied position and would always fail here.
        if (IsValidPlacementSide(@event.World.Reader, @event.X, @event.Y, @event.Z)) return true;

        block.DropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)));
        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        return false;
    }

    private static bool UpdateState(Block block, IWorldContext level, int x, int y, int z)
    {
        int meta = level.Reader.GetBlockMeta(x, y, z);
        int facing = meta & 7;
        int pressToggle = 8 - (meta & 8);
        if (pressToggle == 0) return true;

        level.Writer.SetBlockMeta(x, y, z, facing + pressToggle);
        level.Broadcaster.SetBlocksDirty(x, y, z, x, y, z);
        level.Broadcaster.PlaySoundAtPos(x + 0.5D, y + 0.5D, z + 0.5D, "random.click", 0.3F, 0.6F);
        level.Broadcaster.NotifyNeighbors(x, y, z, block.id);
        switch (facing)
        {
            case 1:
                level.Broadcaster.NotifyNeighbors(x - 1, y, z, block.id);
                break;
            case 2:
                level.Broadcaster.NotifyNeighbors(x + 1, y, z, block.id);
                break;
            case 3:
                level.Broadcaster.NotifyNeighbors(x, y, z - 1, block.id);
                break;
            case 4:
                level.Broadcaster.NotifyNeighbors(x, y, z + 1, block.id);
                break;
            default:
                level.Broadcaster.NotifyNeighbors(x, y - 1, z, block.id);
                break;
        }

        level.TickScheduler.ScheduleBlockUpdate(x, y, z, block.id, block.TickRate);
        return true;
    }
}
