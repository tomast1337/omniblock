using BetaSharp.Entities;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Detector rail: powers (metadata bit 8) while a minecart sits inside the detection volume,
///     re-checking every <c>getTickRate()</c> ticks. Track shape and rendering stay in
///     <see cref="RailBehavior" />. Assign to the Redstone, Interactable, and Ticker slots.
/// </summary>
public sealed class DetectorRailBehavior : IRedstoneComponent, IBlockInteractable, IBlockTicker
{
    private const float DetectionInset = 2.0F / 16.0F;

    public void OnEntityCollision(Block block, OnEntityCollisionEvent @event)
    {
        if (@event.World.IsRemote) return;

        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        if ((meta & 8) == 0)
        {
            UpdatePoweredStatus(block, @event.World, @event.X, @event.Y, @event.Z, meta);
        }
    }

    public void OnTick(Block block, OnTickEvent @event)
    {
        if (@event.World.IsRemote) return;

        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        if ((meta & 8) != 0)
        {
            UpdatePoweredStatus(block, @event.World, @event.X, @event.Y, @event.Z, meta);
        }
    }

    public bool IsPoweringSide(Block block, IBlockReader reader, int x, int y, int z, int side) => (reader.GetBlockMeta(x, y, z) & 8) != 0;

    public bool IsStrongPoweringSide(Block block, IBlockReader world, int x, int y, int z, int side) => (world.GetBlockMeta(x, y, z) & 8) != 0 && side == 1;

    public bool CanEmitRedstonePower(Block block) => true;

    private static void UpdatePoweredStatus(Block block, IWorldContext context, int x, int y, int z, int meta)
    {
        bool isPowered = (meta & 8) != 0;
        bool hasMinecart = false;

        List<EntityMinecart> minecartsOnRail = context.Entities.CollectEntitiesOfType<EntityMinecart>(new Box(x + DetectionInset, y, z + DetectionInset, x + 1 - DetectionInset, y + 0.25D, z + 1 - DetectionInset));
        if (minecartsOnRail.Count > 0) hasMinecart = true;

        if (hasMinecart && !isPowered)
        {
            context.Writer.SetBlockMeta(x, y, z, meta | 8);
            context.Broadcaster.NotifyNeighbors(x, y, z, block.id);
            context.Broadcaster.NotifyNeighbors(x, y - 1, z, block.id);
            context.Broadcaster.SetBlocksDirty(x, y, z, x, y, z);
        }

        if (!hasMinecart && isPowered)
        {
            context.Writer.SetBlockMeta(x, y, z, meta & 7);
            context.Broadcaster.NotifyNeighbors(x, y, z, block.id);
            context.Broadcaster.NotifyNeighbors(x, y - 1, z, block.id);
            context.Broadcaster.SetBlocksDirty(x, y, z, x, y, z);
        }

        if (hasMinecart)
        {
            context.TickScheduler.ScheduleBlockUpdate(x, y, z, block.id, block.TickRate);
        }
    }
}
