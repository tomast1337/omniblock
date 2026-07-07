using BetaSharp.Blocks.Behaviors;

namespace BetaSharp.Blocks;

/// <summary>
/// Stays a <see cref="BlockTorch"/> subclass for the wall-mount placement/facing machinery;
/// power emission, burnout, and toggling live in the shared <see cref="RedstoneTorchBehavior"/>.
/// </summary>
internal class BlockRedstoneTorch : BlockTorch
{
    // One shared instance across the lit and unlit blocks — the burnout history must span
    // both, since a toggling torch alternates between the two block ids.
    private static readonly RedstoneTorchBehavior s_behavior = new();

    private readonly bool _lit;

    public BlockRedstoneTorch(int id, int textureId, bool lit) : base(id, textureId)
    {
        _lit = lit;
        setTickRandomly(true);
        setTickRate(2);
        setDrops(() => LitRedstoneTorch.id);
        SetRedstone(s_behavior);
        SetTicker(s_behavior);
        SetVisuals(s_behavior);
    }

    public override void onPlaced(OnPlacedEvent @event)
    {
        if (@event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z) == 0) base.onPlaced(@event);

        if (!_lit) return;

        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y - 1, @event.Z, id);
        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y + 1, @event.Z, id);
        @event.World.Broadcaster.NotifyNeighbors(@event.X - 1, @event.Y, @event.Z, id);
        @event.World.Broadcaster.NotifyNeighbors(@event.X + 1, @event.Y, @event.Z, id);
        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y, @event.Z - 1, id);
        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y, @event.Z + 1, id);
    }

    public override void onBreak(OnBreakEvent @event)
    {
        if (!_lit) return;

        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y - 1, @event.Z, id);
        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y + 1, @event.Z, id);
        @event.World.Broadcaster.NotifyNeighbors(@event.X - 1, @event.Y, @event.Z, id);
        @event.World.Broadcaster.NotifyNeighbors(@event.X + 1, @event.Y, @event.Z, id);
        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y, @event.Z - 1, id);
        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y, @event.Z + 1, id);
    }

    public override void neighborUpdate(OnTickEvent @event)
    {
        base.neighborUpdate(@event);
        @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, id, getTickRate());
    }

    // BlockTorch draws flame particles; replace them with the behavior's reddust.
    public override void randomDisplayTick(OnTickEvent @event) => Ticker?.RandomDisplayTick(this, @event);
}
