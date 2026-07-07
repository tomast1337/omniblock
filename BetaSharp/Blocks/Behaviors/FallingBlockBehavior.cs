using BetaSharp.Blocks.Materials;
using BetaSharp.Entities;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Gravity-affected blocks (sand, gravel): schedules a fall check when placed or when a
///     neighbor changes, and falls on tick. Spans three capabilities — assign the same instance
///     to the Ticker, Lifecycle, and Physics slots.
/// </summary>
public class FallingBlockBehavior : IBlockTicker, IBlockLifecycle, IBlockPhysics
{
    private const sbyte CheckRadius = 32;
    private static readonly ThreadLocal<bool> s_fallInstantly = new(() => false);

    public static bool FallInstantly
    {
        get => s_fallInstantly.Value;
        set => s_fallInstantly.Value = value;
    }

    public void OnPlaced(Block block, OnPlacedEvent @event) => @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, block.Id, block.GetTickRate());

    public void NeighborUpdate(Block block, OnTickEvent @event) => @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, block.Id, block.GetTickRate());

    public void OnTick(Block block, OnTickEvent @event) => ProcessFall(block, @event);

    private static void ProcessFall(Block block, OnTickEvent @event)
    {
        (int x, int y, int z) = (@event.X, @event.Y, @event.Z);
        if (y <= 0 || !CanFallThrough(new OnTickEvent(@event.World, x, y - 1, z, 0, @event.BlockId))) return;

        if (!FallInstantly && @event.World.ChunkHost.IsRegionLoaded(x - CheckRadius, y - CheckRadius, z - CheckRadius, x + CheckRadius, y + CheckRadius, z + CheckRadius))
        {
            EntityFallingSand fallingSand = new(@event.World, x + 0.5F, y + 0.5F, z + 0.5F, block.Id);
            @event.World.Entities.SpawnEntity(fallingSand);
        }
        else
        {
            @event.World.Writer.SetBlock(x, y, z, 0);

            while (CanFallThrough(new OnTickEvent(@event.World, x, y - 1, z, 0, @event.BlockId)) && y > 0)
            {
                --y;
            }

            if (y > 0)
            {
                @event.World.Writer.SetBlock(x, y, z, block.Id);
            }
        }
    }

    public static bool CanFallThrough(OnTickEvent ctx)
    {
        int blockId = ctx.World.Reader.GetBlockId(ctx.X, ctx.Y, ctx.Z);
        if (blockId == 0) return true;
        if (blockId == Block.Fire.Id) return true;

        Material material = Block.Blocks[blockId].Material;
        return material == Material.Water || material == Material.Lava;
    }
}
