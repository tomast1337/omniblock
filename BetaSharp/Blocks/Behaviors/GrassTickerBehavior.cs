namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Grass spread and death: dies in low light when covered, spreads to adjacent dirt in high light.
/// </summary>
public sealed class GrassTickerBehavior : IBlockTicker
{
    public void OnTick(Block block, OnTickEvent ctx)
    {
        if (ctx.World.IsRemote) return;

        if (ctx.World.Lighting.GetLightLevel(ctx.X, ctx.Y + 1, ctx.Z) < 4 && Block.BlockLightOpacity[ctx.World.Reader.GetBlockId(ctx.X, ctx.Y + 1, ctx.Z)] > 2)
        {
            if (Random.Shared.Next(4) != 0) return;

            ctx.World.Writer.SetBlock(ctx.X, ctx.Y, ctx.Z, Block.Dirt.id);
        }
        else if (ctx.World.Lighting.GetLightLevel(ctx.X, ctx.Y + 1, ctx.Z) >= 9)
        {
            int spreadX = ctx.X + Random.Shared.Next(3) - 1;
            int spreadY = ctx.Y + Random.Shared.Next(5) - 3;
            int spreadZ = ctx.Z + Random.Shared.Next(3) - 1;
            int blockAboveId = ctx.World.Reader.GetBlockId(spreadX, spreadY + 1, spreadZ);
            if (ctx.World.Reader.GetBlockId(spreadX, spreadY, spreadZ) == Block.Dirt.id && ctx.World.Lighting.GetLightLevel(spreadX, spreadY + 1, spreadZ) >= 4 && Block.BlockLightOpacity[blockAboveId] <= 2)
            {
                ctx.World.Writer.SetBlock(spreadX, spreadY, spreadZ, Block.GrassBlock.id);
            }
        }
    }
}
