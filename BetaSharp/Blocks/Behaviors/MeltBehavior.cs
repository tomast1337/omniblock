namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Light-driven melting (ice, snow): when block light exceeds the threshold on a random tick,
/// drops the block's items and replaces it. Assign to the Ticker slot — and the Lifecycle slot
/// too when <paramref name="brokenReplacement"/> is set (ice turning to water when mined).
/// </summary>
/// <param name="meltReplacement">
/// Block id to melt into (deferred so it can reference block statics regardless of declaration order).
/// </param>
/// <param name="subtractOpacity">
/// When true the melt threshold is <c>11 - BlockLightOpacity[id]</c> (ice); otherwise a flat 11 (snow).
/// </param>
/// <param name="brokenReplacement">
/// Optional block id placed after the block is mined over solid or fluid ground (ice → flowing water).
/// </param>
public sealed class MeltBehavior(Func<int> meltReplacement, bool subtractOpacity = false, Func<int>? brokenReplacement = null) : IBlockTicker, IBlockLifecycle
{
    public void OnTick(Block block, OnTickEvent @event)
    {
        int threshold = subtractOpacity ? 11 - Block.BlockLightOpacity[block.id] : 11;
        if (@event.World.Lighting.GetBrightness(LightType.Block, @event.X, @event.Y, @event.Z) <= threshold)
        {
            return;
        }

        block.dropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)));
        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, meltReplacement());
    }

    public void OnAfterBreak(Block block, OnAfterBreakEvent @event)
    {
        if (brokenReplacement == null) return;

        Materials.Material materialBelow = @event.World.Reader.GetMaterial(@event.X, @event.Y - 1, @event.Z);
        if (materialBelow.BlocksMovement || materialBelow.IsFluid)
        {
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, brokenReplacement());
        }
    }
}
