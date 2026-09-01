using OmniBlock.Entities.Behaviors;
using OmniBlock.Items;
using OmniBlock.Rules;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Wheat crop growth: moisture-driven stage progression (metadata 0-7), farmland-only survival,
///     and bonus seed scatter on harvest. Self-contained (does not compose with
///     <see cref="PlantSurvivalBehavior" />) because its growth/survival predicate is farmland-specific
///     and its own <see cref="CanGrow" /> hook must be reachable from <see cref="NeighborUpdate" />.
///     <para>
///         Required soil (<paramref name="requiredSoil" />), mature-drop item
///         (<paramref name="matureCropItem" />), and seed item (<paramref name="seeds" />) are all
///         required, (see <c>BehaviorRegistry</c>'s <c>"crop"</c>
///         entry).
///     </para>
///     <para>
///         Drop spread (<paramref name="dropSpread" />), bonus-seed chance bound
///         (<paramref name="seedScatterChanceBound" />, rolled against the block's meta at drop
///         time, higher meta means a better chance per attempt), and growth-chance denominator
///         (<paramref name="growthChanceDenominator" />, 1-in-N per available-moisture-unit per
///         tick) are also required.
///     </para>
/// </summary>
internal sealed class CropBehavior(Block requiredSoil, Item matureCropItem, Item seeds, float dropSpread, int seedScatterChanceBound, int growthChanceDenominator, int[] stages) : IBlockTicker, IBlockPhysics, IBlockLifecycle, IBlockVisuals
{
    public int GetDroppedItemId(Block block, int blockMeta, int defaultItemId)
    {
        return blockMeta == 7 ? matureCropItem.Id : -1;
    }

    public void OnDropStacks(Block block, OnDropEvent @event)
    {
        if (@event.World.IsRemote || !@event.World.Rules.GetBool(DefaultRules.DoTileDrops))
            return;

        for (var attempt = 0; attempt < 3; ++attempt)
        {
            if (Random.Shared.Next(seedScatterChanceBound) > @event.Meta)
                continue;

            var offsetX = Random.Shared.NextSingle() * dropSpread + (1.0F - dropSpread) * 0.5F;
            var offsetY = Random.Shared.NextSingle() * dropSpread + (1.0F - dropSpread) * 0.5F;
            var offsetZ = Random.Shared.NextSingle() * dropSpread + (1.0F - dropSpread) * 0.5F;
            var entityItem = DroppedItemBehavior.Create(@event.World, @event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ, new ItemStack(seeds), 10);
            @event.World.Entities.SpawnEntity(entityItem);
        }
    }

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event)
    {
        return @event.World.Reader.GetBlockId(@event.X, @event.Y - 1, @event.Z) == requiredSoil.Id;
    }

    public bool CanGrow(Block block, OnTickEvent ctx)
    {
        return (ctx.World.Reader.GetBrightness(ctx.X, ctx.Y, ctx.Z) >= 8 || ctx.World.Lighting.HasSkyLight(ctx.X, ctx.Y, ctx.Z))
               && ctx.World.Reader.GetBlockId(ctx.X, ctx.Y - 1, ctx.Z) == requiredSoil.Id;
    }

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        BreakIfCannotSurvive(block, @event.World, @event.X, @event.Y, @event.Z);
    }

    public void OnTick(Block block, OnTickEvent @event)
    {
        BreakIfCannotSurvive(block, @event.World, @event.X, @event.Y, @event.Z);

        if (@event.World.Lighting.GetBrightness(LightType.Block, @event.X, @event.Y + 1, @event.Z) < 9) return;

        var meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        if (meta >= 7) return;

        var moisture = GetAvailableMoisture(block, @event.World.Reader, @event.X, @event.Y, @event.Z);
        if (Random.Shared.Next(growthChanceDenominator) / moisture != 0) return;

        ++meta;
        @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, meta);
    }

    // A negative age means "fully grown" to the item renderer, which has no crop to measure.
    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
    {
        return meta < 0 ? stages[^1] : stages[meta];
    }

    private float GetAvailableMoisture(Block block, IBlockReader read, int x, int y, int z)
    {
        var totalMoisture = 1.0F;
        var blockNorth = read.GetBlockId(x, y, z - 1);
        var blockSouth = read.GetBlockId(x, y, z + 1);
        var blockWest = read.GetBlockId(x - 1, y, z);
        var blockEast = read.GetBlockId(x + 1, y, z);
        var blockNorthWest = read.GetBlockId(x - 1, y, z - 1);
        var blockNorthEast = read.GetBlockId(x + 1, y, z - 1);
        var blockSouthEast = read.GetBlockId(x + 1, y, z + 1);
        var blockSouthWest = read.GetBlockId(x - 1, y, z + 1);
        var cropsEastWest = blockWest == block.Id || blockEast == block.Id;
        var cropsNorthSouth = blockNorth == block.Id || blockSouth == block.Id;
        var cropsDiagonals = blockNorthWest == block.Id || blockNorthEast == block.Id || blockSouthEast == block.Id || blockSouthWest == block.Id;

        for (var dx = x - 1; dx <= x + 1; ++dx)
        for (var dz = z - 1; dz <= z + 1; ++dz)
        {
            var blockBelow = read.GetBlockId(dx, y - 1, dz);
            var cellMoisture = 0.0F;
            if (blockBelow == requiredSoil.Id)
            {
                cellMoisture = 1.0F;
                if (read.GetBlockMeta(dx, y - 1, dz) > 0) cellMoisture = 3.0F;
            }

            if (dx != x || dz != z) cellMoisture /= 4.0F;

            totalMoisture += cellMoisture;
        }

        if (cropsDiagonals || (cropsEastWest && cropsNorthSouth)) totalMoisture /= 2.0F;

        return totalMoisture;
    }

    /// <summary>Instant full-growth for bone meal (<c>ItemDye</c>).</summary>
    public static void ApplyFullGrowth(IWorldContext world, int x, int y, int z)
    {
        world.Writer.SetBlockMeta(x, y, z, 7);
    }

    private static void BreakIfCannotSurvive(Block block, IWorldContext level, int x, int y, int z)
    {
        if (block.CanGrow(new OnTickEvent(level, x, y, z, level.Reader.GetBlockMeta(x, y, z), level.Reader.GetBlockId(x, y, z))))
            return;

        block.DropStacks(new OnDropEvent(level, x, y, z, level.Reader.GetBlockMeta(x, y, z)));
        level.Writer.SetBlock(x, y, z, 0);
    }
}