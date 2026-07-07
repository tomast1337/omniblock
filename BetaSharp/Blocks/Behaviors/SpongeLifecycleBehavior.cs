namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Sponge removal notifies every block in a 2-block radius so absorbed water can flow back.
///     (The Beta 1.7.3 on-place absorption loop was an empty no-op and is intentionally not ported.)
/// </summary>
public sealed class SpongeLifecycleBehavior : IBlockLifecycle
{
    private const sbyte AbsorbRadius = 2;

    public void OnBreak(Block block, OnBreakEvent @event)
    {
        for (int checkX = @event.X - AbsorbRadius; checkX <= @event.X + AbsorbRadius; ++checkX)
        {
            for (int checkY = @event.Y - AbsorbRadius; checkY <= @event.Y + AbsorbRadius; ++checkY)
            {
                for (int checkZ = @event.Z - AbsorbRadius; checkZ <= @event.Z + AbsorbRadius; ++checkZ)
                {
                    @event.World.Broadcaster.NotifyNeighbors(checkX, checkY, checkZ, @event.World.Reader.GetBlockId(checkX, checkY, checkZ));
                }
            }
        }
    }
}
