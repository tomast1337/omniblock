namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Sponge removal notifies every block in a radius so absorbed water can flow back.
///     (The Beta 1.7.3 on-place absorption loop was an empty no-op and is intentionally not ported.)
///     <para>
///         Absorb radius (<paramref name="absorbRadius" />) is a required, param (see <c>BehaviorRegistry</c>'s <c>"sponge_lifecycle"</c> entry).
///     </para>
/// </summary>
public sealed class SpongeLifecycleBehavior(int absorbRadius) : IBlockLifecycle
{
    public void OnBreak(Block block, OnBreakEvent @event)
    {
        for (int checkX = @event.X - absorbRadius; checkX <= @event.X + absorbRadius; ++checkX)
        {
            for (int checkY = @event.Y - absorbRadius; checkY <= @event.Y + absorbRadius; ++checkY)
            {
                for (int checkZ = @event.Z - absorbRadius; checkZ <= @event.Z + absorbRadius; ++checkZ)
                {
                    @event.World.Broadcaster.NotifyNeighbors(checkX, checkY, checkZ, @event.World.Reader.GetBlockId(checkX, checkY, checkZ));
                }
            }
        }
    }
}
