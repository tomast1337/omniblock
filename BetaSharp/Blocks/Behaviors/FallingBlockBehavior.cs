using BetaSharp.Blocks.Materials;
using BetaSharp.Entities;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Gravity-affected blocks (sand, gravel): schedules a fall check when placed or when a
///     neighbor changes, and falls on tick. Spans three capabilities.
///     <para>
///         Non-solid obstacles it falls through (vanilla: just fire) are a required,
///         (see <c>BehaviorRegistry</c>'s <c>"falling_block"</c> entry).
///         <see cref="CanFallThrough" /> is called externally by <c>EntityFallingSand</c> (the
///         falling block only knows its own block id at that point, not a behavior instance), so
///         it resolves back to this instance via <c>Block.Blocks[id].Physics</c> rather than
///         taking a static, hardcoded set.
///     </para>
///     <para>
///         Region-loaded check radius (<paramref name="regionLoadCheckRadius" />) is a required.
///     </para>
/// </summary>
public class FallingBlockBehavior(Block[] passable, int regionLoadCheckRadius) : IBlockTicker, IBlockLifecycle, IBlockPhysics
{
    private static readonly ThreadLocal<bool> s_fallInstantly = new(() => false);

    public static bool FallInstantly
    {
        get => s_fallInstantly.Value;
        set => s_fallInstantly.Value = value;
    }

    public void OnPlaced(Block block, OnPlacedEvent @event) => @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, block.id, block.TickRate);

    public void NeighborUpdate(Block block, OnTickEvent @event) => @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, block.id, block.TickRate);

    public void OnTick(Block block, OnTickEvent @event) => ProcessFall(block, @event);

    private void ProcessFall(Block block, OnTickEvent @event)
    {
        (int x, int y, int z) = (@event.X, @event.Y, @event.Z);
        if (y <= 0 || !CanFallThrough(new OnTickEvent(@event.World, x, y - 1, z, 0, @event.BlockId))) return;

        if (!FallInstantly && @event.World.ChunkHost.IsRegionLoaded(x - regionLoadCheckRadius, y - regionLoadCheckRadius, z - regionLoadCheckRadius, x + regionLoadCheckRadius, y + regionLoadCheckRadius, z + regionLoadCheckRadius))
        {
            EntityFallingSand fallingSand = new(@event.World, x + 0.5F, y + 0.5F, z + 0.5F, block.id);
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
                @event.World.Writer.SetBlock(x, y, z, block.id);
            }
        }
    }

    public bool CanFallThrough(OnTickEvent ctx)
    {
        int blockId = ctx.World.Reader.GetBlockId(ctx.X, ctx.Y, ctx.Z);
        if (blockId == 0) return true;

        foreach (Block obstacle in passable)
        {
            if (blockId == obstacle.id) return true;
        }

        Material material = Block.Blocks[blockId].material;
        return material == Material.Water || material == Material.Lava;
    }
}
