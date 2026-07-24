using BetaSharp.Entities;
using BetaSharp.Items;
using BetaSharp.Rules;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Wheat crop growth: moisture-driven stage progression (metadata 0-7), farmland-only survival,
///     and bonus seed scatter on harvest. Self-contained (does not compose with
///     <see cref="PlantSurvivalBehavior" />) because its growth/survival predicate is farmland-specific
///     and its own <see cref="CanGrow" /> hook must be reachable from <see cref="NeighborUpdate" />.
///     <para>
///         Required soil (<paramref name="requiredSoil" />), mature-drop item
///         (<paramref name="matureCropItem" />), and seed item (<paramref name="seeds" />) are all
///         required, JSON-declared constructor params (see <c>BehaviorRegistry</c>'s <c>"crop"</c>
///         entry) — no built-in vanilla fallback; an omitted or unknown name throws immediately at
///         startup. Named generically (not "farmland"/"wheat") so a non-vanilla crop variant reads
///         naturally.
///     </para>
///     <para>
///         Drop spread (<paramref name="dropSpread" />), bonus-seed chance bound
///         (<paramref name="seedScatterChanceBound" />, rolled against the block's meta at drop
///         time — higher meta means a better chance per attempt), and growth-chance denominator
///         (<paramref name="growthChanceDenominator" />, 1-in-N per available-moisture-unit per
///         tick) are also required, JSON-declared constructor params — no built-in vanilla
///         fallback.
///     </para>
/// </summary>
internal sealed class CropBehavior(Block requiredSoil, Item matureCropItem, Item seeds, float dropSpread, int seedScatterChanceBound, int growthChanceDenominator) : IBlockTicker, IBlockPhysics, IBlockLifecycle, IBlockVisuals
{
    public int GetDroppedItemId(Block block, int blockMeta, int defaultItemId) => blockMeta == 7 ? matureCropItem.Id : -1;

    public void OnDropStacks(Block block, OnDropEvent @event)
    {
        if (@event.World.IsRemote || !@event.World.Rules.GetBool(DefaultRules.DoTileDrops))
            return;

        for (int attempt = 0; attempt < 3; ++attempt)
        {
            if (Random.Shared.Next(seedScatterChanceBound) > @event.Meta)
                continue;

            float offsetX = Random.Shared.NextSingle() * dropSpread + (1.0F - dropSpread) * 0.5F;
            float offsetY = Random.Shared.NextSingle() * dropSpread + (1.0F - dropSpread) * 0.5F;
            float offsetZ = Random.Shared.NextSingle() * dropSpread + (1.0F - dropSpread) * 0.5F;
            EntityItem entityItem = new(@event.World, @event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ, new ItemStack(seeds))
            {
                DelayBeforeCanPickup = 10
            };
            @event.World.Entities.SpawnEntity(entityItem);
        }
    }

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event)
        => @event.World.Reader.GetBlockId(@event.X, @event.Y - 1, @event.Z) == requiredSoil.id;

    public bool CanGrow(Block block, OnTickEvent ctx)
        => (ctx.World.Reader.GetBrightness(ctx.X, ctx.Y, ctx.Z) >= 8 || ctx.World.Lighting.HasSkyLight(ctx.X, ctx.Y, ctx.Z))
           && ctx.World.Reader.GetBlockId(ctx.X, ctx.Y - 1, ctx.Z) == requiredSoil.id;

    public void NeighborUpdate(Block block, OnTickEvent @event)
        => BreakIfCannotSurvive(block, @event.World, @event.X, @event.Y, @event.Z);

    public void OnTick(Block block, OnTickEvent @event)
    {
        BreakIfCannotSurvive(block, @event.World, @event.X, @event.Y, @event.Z);

        if (@event.World.Lighting.GetBrightness(LightType.Block, @event.X, @event.Y + 1, @event.Z) < 9) return;

        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        if (meta >= 7) return;

        float moisture = GetAvailableMoisture(block, @event.World.Reader, @event.X, @event.Y, @event.Z);
        if (Random.Shared.Next(growthChanceDenominator) / moisture != 0) return;

        ++meta;
        @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, meta);
    }

    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
        => defaultTexture + (meta < 0 ? 7 : meta);

    private float GetAvailableMoisture(Block block, IBlockReader read, int x, int y, int z)
    {
        float totalMoisture = 1.0F;
        int blockNorth = read.GetBlockId(x, y, z - 1);
        int blockSouth = read.GetBlockId(x, y, z + 1);
        int blockWest = read.GetBlockId(x - 1, y, z);
        int blockEast = read.GetBlockId(x + 1, y, z);
        int blockNorthWest = read.GetBlockId(x - 1, y, z - 1);
        int blockNorthEast = read.GetBlockId(x + 1, y, z - 1);
        int blockSouthEast = read.GetBlockId(x + 1, y, z + 1);
        int blockSouthWest = read.GetBlockId(x - 1, y, z + 1);
        bool cropsEastWest = blockWest == block.id || blockEast == block.id;
        bool cropsNorthSouth = blockNorth == block.id || blockSouth == block.id;
        bool cropsDiagonals = blockNorthWest == block.id || blockNorthEast == block.id || blockSouthEast == block.id || blockSouthWest == block.id;

        for (int dx = x - 1; dx <= x + 1; ++dx)
        {
            for (int dz = z - 1; dz <= z + 1; ++dz)
            {
                int blockBelow = read.GetBlockId(dx, y - 1, dz);
                float cellMoisture = 0.0F;
                if (blockBelow == requiredSoil.id)
                {
                    cellMoisture = 1.0F;
                    if (read.GetBlockMeta(dx, y - 1, dz) > 0)
                    {
                        cellMoisture = 3.0F;
                    }
                }

                if (dx != x || dz != z)
                {
                    cellMoisture /= 4.0F;
                }

                totalMoisture += cellMoisture;
            }
        }

        if (cropsDiagonals || (cropsEastWest && cropsNorthSouth))
        {
            totalMoisture /= 2.0F;
        }

        return totalMoisture;
    }

    /// <summary>Instant full-growth for bone meal (<c>ItemDye</c>).</summary>
    public static void ApplyFullGrowth(IWorldContext world, int x, int y, int z) => world.Writer.SetBlockMeta(x, y, z, 7);

    private static void BreakIfCannotSurvive(Block block, IWorldContext level, int x, int y, int z)
    {
        if (block.canGrow(new OnTickEvent(level, x, y, z, level.Reader.GetBlockMeta(x, y, z), level.Reader.GetBlockId(x, y, z))))
            return;

        block.DropStacks(new OnDropEvent(level, x, y, z, level.Reader.GetBlockMeta(x, y, z)));
        level.Writer.SetBlock(x, y, z, 0);
    }
}
