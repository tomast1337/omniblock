using BetaSharp.Blocks.Behaviors;
using BetaSharp.Blocks.Materials;

namespace BetaSharp.Blocks;

internal class BlockSand : Block
{
    public BlockSand(int id, int textureId) : base(id, textureId, Material.Sand) => SetTicker(new FallingBlockTicker());

    public override void onPlaced(OnPlacedEvent ctx) => ctx.World.TickScheduler.ScheduleBlockUpdate(ctx.X, ctx.Y, ctx.Z, id, getTickRate());

    public override void neighborUpdate(OnTickEvent ctx) => ctx.World.TickScheduler.ScheduleBlockUpdate(ctx.X, ctx.Y, ctx.Z, id, getTickRate());

    public override int getTickRate() => 3;
}
