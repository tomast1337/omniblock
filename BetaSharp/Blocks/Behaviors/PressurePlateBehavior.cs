using BetaSharp.Entities;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Pressure plate: presses (metadata 1) while entities matching the activation rule stand on it,
/// emits power while pressed, and pops back out after <c>getTickRate()</c> ticks without weight.
/// Assign to the Redstone, Interactable, Ticker, Physics, and Lifecycle slots.
/// </summary>
public sealed class PressurePlateBehavior(PressurePlateActiviationRule activationRule) : IRedstoneComponent, IBlockInteractable, IBlockTicker, IBlockPhysics, IBlockLifecycle
{
    private const float EdgeInset = 1.0F / 16.0F;
    private const float HalfWidth = 0.5F;
    private const float HalfHeight = 2.0F / 16.0F;
    private const float HalfDepth = 0.5F;

    private const float DetectionInset = 2.0F / 16.0F;

    public bool CanPlaceAt(Block block, CanPlaceAtContext context) => context.World.Reader.ShouldSuffocate(context.X, context.Y - 1, context.Z);

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        bool shouldBreak = !@event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z);

        if (!shouldBreak) return;

        block.dropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)));
        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
    }

    public void OnTick(Block block, OnTickEvent @event)
    {
        bool wasPressed = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z) > 0;
        if (wasPressed)
        {
            UpdatePlateState(block, @event.World, @event.X, @event.Y, @event.Z, wasPressed);
        }
    }

    public void OnMetadataChange(Block block, OnMetadataChangeEvent @event)
    {
        if (!@event.World.IsRemote) return;

        if (@event.Meta == 0)
        {
            UpdatePlateState(block, @event.World, @event.X, @event.Y, @event.Z, true, false);
        }
    }

    public void OnEntityCollision(Block block, OnEntityCollisionEvent @event)
    {
        bool wasPressed = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z) > 0;
        if (!wasPressed)
        {
            UpdatePlateState(block, @event.World, @event.X, @event.Y, @event.Z, wasPressed);
        }
    }

    private void UpdatePlateState(Block block, IWorldContext ctx, int x, int y, int z, bool wasPressed)
    {
        bool shouldBePressed = activationRule switch
        {
            PressurePlateActiviationRule.EVERYTHING => ctx.Entities.CollectEntitiesOfType<Entity>(new Box(x + DetectionInset, y, z + DetectionInset, x + 1 - DetectionInset, y + 0.25D, z + 1 - DetectionInset)).Any(),
            PressurePlateActiviationRule.MOBS => ctx.Entities.CollectEntitiesOfType<EntityLiving>(new Box(x + DetectionInset, y, z + DetectionInset, x + 1 - DetectionInset, y + 0.25D, z + 1 - DetectionInset)).Any(),
            PressurePlateActiviationRule.PLAYERS => ctx.Entities.CollectEntitiesOfType<EntityPlayer>(new Box(x + DetectionInset, y, z + DetectionInset, x + 1 - DetectionInset, y + 0.25D, z + 1 - DetectionInset)).Any(),
            _ => false
        };

        UpdatePlateState(block, ctx, x, y, z, wasPressed, shouldBePressed);
    }

    private static void UpdatePlateState(Block block, IWorldContext ctx, int x, int y, int z, bool wasPressed, bool shouldBePressed)
    {
        if (shouldBePressed != wasPressed)
        {
            ctx.Writer.SetBlockMeta(x, y, z, shouldBePressed ? 1 : 0);
            if (!ctx.IsRemote)
            {
                ctx.Broadcaster.NotifyNeighborsFloor(x, y, z, block.id);
                ctx.Broadcaster.SetBlocksDirty(x, y, z, x, y, z);
            }
            else
            {
                ctx.Broadcaster.PlaySoundAtPos(x + 0.5D, y + 0.1D, z + 0.5D, "random.click", 0.3F, shouldBePressed ? 0.6f : 0.5f);
            }
        }

        if (shouldBePressed)
        {
            ctx.TickScheduler.ScheduleBlockUpdate(x, y, z, block.id, block.getTickRate());
        }
    }

    public void OnBreak(Block block, OnBreakEvent @event)
    {
        int plateState = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        if (plateState > 0)
        {
            @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y, @event.Z, block.id);
            @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y - 1, @event.Z, block.id);
        }
    }

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        bool isPressed = reader.GetBlockMeta(x, y, z) == 1;
        if (isPressed)
        {
            block.setBoundingBox(EdgeInset, 0.0F, EdgeInset, 1.0F - EdgeInset, 1 / 32f, 1.0F - EdgeInset);
        }
        else
        {
            block.setBoundingBox(EdgeInset, 0.0F, EdgeInset, 1.0F - EdgeInset, 1.0F / 16.0F, 1.0F - EdgeInset);
        }
    }

    public void SetupRenderBoundingBox(Block block) => block.setBoundingBox(0.5F - HalfWidth, 0.5F - HalfHeight, 0.5F - HalfDepth, 0.5F + HalfWidth, 0.5F + HalfHeight, 0.5F + HalfDepth);

    public bool IsPoweringSide(Block block, IBlockReader reader, int x, int y, int z, int side) => reader.GetBlockMeta(x, y, z) > 0;

    public bool IsStrongPoweringSide(Block block, IBlockReader world, int x, int y, int z, int side) => world.GetBlockMeta(x, y, z) != 0 && side == 1;

    public bool CanEmitRedstonePower(Block block) => true;
}
