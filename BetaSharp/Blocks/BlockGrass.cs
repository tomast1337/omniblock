using BetaSharp.Blocks.Behaviors;
using BetaSharp.Blocks.Materials;

namespace BetaSharp.Blocks;

public class BlockGrass : Block
{
    public BlockGrass(int id) : base(id, Material.SolidOrganic)
    {
        TextureId = BlockTextures.GrassSide;
        setTopBottomTextures(BlockTextures.GrassTop, BlockTextures.Dirt);
        SetVisuals(new GrassVisualBehavior());
        setTickRandomly(true);
    }

    public override void onTick(OnTickEvent ctx)
    {
        if (ctx.World.IsRemote) return;

        if (ctx.World.Lighting.GetLightLevel(ctx.X, ctx.Y + 1, ctx.Z) < 4 && BlockLightOpacity[ctx.World.Reader.GetBlockId(ctx.X, ctx.Y + 1, ctx.Z)] > 2)
        {
            if (Random.Shared.Next(4) != 0) return;

            ctx.World.Writer.SetBlock(ctx.X, ctx.Y, ctx.Z, Dirt.id);
        }
        else if (ctx.World.Lighting.GetLightLevel(ctx.X, ctx.Y + 1, ctx.Z) >= 9)
        {
            int spreadX = ctx.X + Random.Shared.Next(3) - 1;
            int spreadY = ctx.Y + Random.Shared.Next(5) - 3;
            int spreadZ = ctx.Z + Random.Shared.Next(3) - 1;
            int blockAboveId = ctx.World.Reader.GetBlockId(spreadX, spreadY + 1, spreadZ);
            if (ctx.World.Reader.GetBlockId(spreadX, spreadY, spreadZ) == Dirt.id && ctx.World.Lighting.GetLightLevel(spreadX, spreadY + 1, spreadZ) >= 4 && BlockLightOpacity[blockAboveId] <= 2)
            {
                ctx.World.Writer.SetBlock(spreadX, spreadY, spreadZ, GrassBlock.id);
            }
        }
    }

    public override int getDroppedItemId(int blocKMeta) => Dirt.getDroppedItemId(0);
}
