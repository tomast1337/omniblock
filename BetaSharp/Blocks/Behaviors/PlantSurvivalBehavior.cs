using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Plant survival: restricts placement to valid ground, and breaks the block (dropping its
///     items) on tick or neighbor change when it can no longer grow, the block below was
///     removed or the light is gone. Assign to both the Ticker and Physics slots.
///     <para>
///         The break check goes through the virtual <see cref="Block.canGrow" />, so subclasses with
///         custom growth rules (mushrooms' darkness requirement) keep them.
///     </para>
///     <para>
///         Valid growth substrate set (<paramref name="validGround" />).
///     </para>
/// </summary>
public sealed class PlantSurvivalBehavior(Block[] validGround) : IBlockTicker, IBlockPhysics
{
    public bool CanPlaceAt(Block block, CanPlaceAtContext @event)
        => IsValidGround(@event.World.Reader.GetBlockId(@event.X, @event.Y - 1, @event.Z));

    public bool CanGrow(Block block, OnTickEvent ctx)
        => (ctx.World.Reader.GetBrightness(ctx.X, ctx.Y, ctx.Z) >= 8 || ctx.World.Lighting.HasSkyLight(ctx.X, ctx.Y, ctx.Z))
           && IsValidGround(ctx.World.Reader.GetBlockId(ctx.X, ctx.Y - 1, ctx.Z));

    public void NeighborUpdate(Block block, OnTickEvent @event) => BreakIfCannotSurvive(block, @event.World, @event.X, @event.Y, @event.Z);

    public void OnTick(Block block, OnTickEvent @event) => BreakIfCannotSurvive(block, @event.World, @event.X, @event.Y, @event.Z);

    private bool IsValidGround(int id)
    {
        foreach (Block ground in validGround)
        {
            if (id == ground.id) return true;
        }

        return false;
    }

    public static void BreakIfCannotSurvive(Block block, IWorldContext level, int x, int y, int z)
    {
        if (block.canGrow(new OnTickEvent(level, x, y, z, level.Reader.GetBlockMeta(x, y, z), level.Reader.GetBlockId(x, y, z)))) return;

        block.DropStacks(new OnDropEvent(level, x, y, z, level.Reader.GetBlockMeta(x, y, z)));
        level.Writer.SetBlock(x, y, z, 0);
    }
}
