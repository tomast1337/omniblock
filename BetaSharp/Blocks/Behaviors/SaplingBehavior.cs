using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;
using BetaSharp.Worlds.Generation.Generators.Features;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Sapling: survival check delegates to <see cref="PlantSurvivalBehavior.BreakIfCannotSurvive" />
///     (assign a plain <see cref="PlantSurvivalBehavior" /> to the Physics slot), then layers the
///     growth-stage bit and tree-generation tick on top. Species is meta bits 0-1; bit 3 is the
///     "ready to grow" flag.
///     <para>
///         <see cref="Generate" /> is called externally by <c>ItemDye</c> for bone meal — kept static and
///         public since there's no subclass left to hold it.
///     </para>
/// </summary>
internal sealed class SaplingBehavior : IBlockTicker, IBlockVisuals, IBlockLifecycle
{
    private static readonly JavaRandom s_random = new();

    public int GetDroppedItemMeta(Block block, int blockMeta, int defaultMeta) => blockMeta & 3;

    public void OnTick(Block block, OnTickEvent @event)
    {
        if (@event.World.IsRemote) return;

        PlantSurvivalBehavior.BreakIfCannotSurvive(block, @event.World, @event.X, @event.Y, @event.Z);
        if (@event.World.Reader.GetBrightness(@event.X, @event.Y + 1, @event.Z) < 9 || Random.Shared.Next(30) != 0) return;
        int saplingMeta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        if ((saplingMeta & 8) == 0)
        {
            @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, saplingMeta | 8);
        }
        else
        {
            Generate(@event.World, @event.X, @event.Y, @event.Z);
        }
    }

    public int GetTexture(Block block, Side side, int meta, int defaultTexture) => (meta & 3) switch
    {
        1 => BlockTextures.SaplingPine,
        2 => BlockTextures.SaplingBirch,
        _ => BlockTextures.SaplingOak
    };

    public static void Generate(IWorldContext world, int x, int y, int z)
    {
        int saplingType = world.Reader.GetBlockMeta(x, y, z) & 3;
        world.Writer.SetBlock(x, y, z, 0);
        Feature treeFeature;
        if (saplingType == 1)
        {
            treeFeature = new SpruceTreeFeature();
        }
        else if (saplingType == 2)
        {
            treeFeature = new BirchTreeFeature();
        }
        else
        {
            treeFeature = new OakTreeFeature();
            if (Random.Shared.Next(10) == 0)
            {
                treeFeature = new LargeOakTreeFeature();
            }
        }

        if (!treeFeature.Generate(world, s_random, x, y, z))
        {
            world.Writer.SetBlock(x, y, z, Block.Sapling.Id, saplingType);
        }
    }
}
