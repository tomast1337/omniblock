namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Grass spread and death: dies in low light when covered, spreads to adjacent dirt in high light.
///     <para>
///         "Dead" state (dirt) is a required, see <c>BehaviorRegistry</c>'s <c>"grass_ticker"</c> entry.
///         The spread target is always the owning <see cref="Block" /> passed into each call (<c>block.id</c>), not a
///         separate
///         cached self-reference, a grass block always spreads into more of itself.
///     </para>
///     <para>
///         Die-back light threshold (<paramref name="dieLightThreshold" />), die chance
///         (<paramref name="dieChanceOneIn" />, 1-in-N per tick once below threshold), and spread
///         light threshold (<paramref name="spreadLightThreshold" />) are also required.
///     </para>
/// </summary>
public sealed class GrassTickerBehavior(Block soil, int dieLightThreshold, int dieChanceOneIn, int spreadLightThreshold) : BlockRuntimeBehavior, IBlockTicker
{
    public void OnTick(Block block, OnTickEvent ctx)
    {
        if (ctx.World.IsRemote) return;

        if (ctx.World.Lighting.GetLightLevel(ctx.X, ctx.Y + 1, ctx.Z) < dieLightThreshold && Blocks.GetOpacity(ctx.World.Reader.GetBlockId(ctx.X, ctx.Y + 1, ctx.Z)) > 2)
        {
            if (Random.Shared.Next(dieChanceOneIn) != 0) return;

            ctx.World.Writer.SetBlock(ctx.X, ctx.Y, ctx.Z, soil.Id);
        }
        else if (ctx.World.Lighting.GetLightLevel(ctx.X, ctx.Y + 1, ctx.Z) >= spreadLightThreshold)
        {
            var spreadX = ctx.X + Random.Shared.Next(3) - 1;
            var spreadY = ctx.Y + Random.Shared.Next(5) - 3;
            var spreadZ = ctx.Z + Random.Shared.Next(3) - 1;
            var blockAboveId = ctx.World.Reader.GetBlockId(spreadX, spreadY + 1, spreadZ);
            if (ctx.World.Reader.GetBlockId(spreadX, spreadY, spreadZ) == soil.Id && ctx.World.Lighting.GetLightLevel(spreadX, spreadY + 1, spreadZ) >= dieLightThreshold && Blocks.GetOpacity(blockAboveId) <= 2)
                ctx.World.Writer.SetBlock(spreadX, spreadY, spreadZ, block.Id);
        }
    }
}
