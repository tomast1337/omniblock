using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

internal sealed class ScriptedTickerBehavior(string hookKey) : IBlockTicker
{
    public void OnTick(Block block, OnTickEvent @event)
    {
        if (!ScriptTickHookRegistry.TryGet(hookKey, out var hook)) return;
        hook(new TickHost(@event.World), @event.X, @event.Y, @event.Z, @event.Meta, @event.BlockId);
    }
}