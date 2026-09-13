using OmniBlock.Blocks.Materials;
using OmniBlock.Entities.Behaviors;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Gravity-affected blocks (sand, gravel): schedules a fall check when placed or when a
///     neighbor changes, and falls on tick. Spans three capabilities.
///     <para>
///         Non-solid obstacles it falls through (vanilla: just fire) are a required,
///         (see <c>BehaviorRegistry</c>'s <c>"falling_block"</c> entry).
///         <see cref="CanFallThrough" /> is called externally by <c>SettleAsBlockBehavior</c> (the
///         falling entity only knows its carried block id at that point, not a behavior instance),
///         so it resolves back to this instance via <c>Blocks.GetByProtocolId(id).Physics</c> rather than
///         taking a static, hardcoded set.
///     </para>
///     <para>
///         Region-loaded check radius (<paramref name="regionLoadCheckRadius" />) is a required.
///     </para>
/// </summary>
public class FallingBlockBehavior(Block[] passable, int regionLoadCheckRadius) : BlockRuntimeBehavior, IBlockTicker, IBlockLifecycle, IBlockPhysics
{
    private static readonly ThreadLocal<int> s_instantFallScopes = new(() => 0);

    public static bool FallInstantly
    {
        get => s_instantFallScopes.Value > 0;
        set => s_instantFallScopes.Value = value ? 1 : 0;
    }

    /// <summary>
    ///     Scopes generation-only falling behavior to the current worker and restores it even when
    ///     a feature throws. The counter makes nested decoration helpers safe.
    /// </summary>
    public static IDisposable BeginInstantFallScope()
    {
        s_instantFallScopes.Value++;
        return new InstantFallScope();
    }

    public void OnPlaced(Block block, OnPlacedEvent @event) => @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, block.Id, block.TickRate);

    public void NeighborUpdate(Block block, OnTickEvent @event) => @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, block.Id, block.TickRate);

    public void OnTick(Block block, OnTickEvent @event) => ProcessFall(block, @event);

    private void ProcessFall(Block block, OnTickEvent @event)
    {
        var (x, y, z) = (@event.X, @event.Y, @event.Z);
        if (y <= 0 || !CanFallThrough(new OnTickEvent(@event.World, x, y - 1, z, 0, @event.BlockId))) return;

        if (!FallInstantly && @event.World.ChunkHost.IsRegionLoaded(x - regionLoadCheckRadius, y - regionLoadCheckRadius, z - regionLoadCheckRadius, x + regionLoadCheckRadius, y + regionLoadCheckRadius, z + regionLoadCheckRadius))
        {
            var fallingSand = @event.World.Content.EntityTypes.Create("omniblock:fallingsand", @event.World);
            fallingSand.Behaviors.Find<SettleAsBlockBehavior>()!.SetBlock(fallingSand, block.Id);
            fallingSand.SetPositionAndAngles(x + 0.5F, y + 0.5F, z + 0.5F, 0.0F, 0.0F);
            @event.World.Entities.SpawnEntity(fallingSand);
        }
        else
        {
            @event.World.Writer.SetBlock(x, y, z, 0);

            while (CanFallThrough(new OnTickEvent(@event.World, x, y - 1, z, 0, @event.BlockId)) && y > 0) --y;

            if (y > 0) @event.World.Writer.SetBlock(x, y, z, block.Id);
        }
    }

    public bool CanFallThrough(OnTickEvent ctx)
    {
        var blockId = ctx.World.Reader.GetBlockId(ctx.X, ctx.Y, ctx.Z);
        if (blockId == 0) return true;

        foreach (var obstacle in passable)
        {
            if (blockId == obstacle.Id)
                return true;
        }

        var material = Blocks.GetByProtocolId(blockId).Material;
        return material == Material.Water || material == Material.Lava;
    }

    private sealed class InstantFallScope : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            s_instantFallScopes.Value = Math.Max(0, s_instantFallScopes.Value - 1);
        }
    }
}
