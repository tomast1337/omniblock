using BetaSharp.Entities;
using BetaSharp.Items;
using BetaSharp.Rules;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Wheat crop growth: moisture-driven stage progression (metadata 0-7), farmland-only survival,
/// and bonus seed scatter on harvest. Self-contained (does not compose with
/// <see cref="PlantSurvivalBehavior"/>) because its growth/survival predicate is farmland-specific
/// and its own <see cref="CanGrow"/> hook must be reachable from <see cref="NeighborUpdate"/>.
/// </summary>
internal sealed class CropBehavior : IBlockTicker, IBlockPhysics, IBlockLifecycle, IBlockVisuals
{
    private const float DropSpread = 0.7F;

    // ── IBlockTicker ──────────────────────────────────────────────

    public void OnTick(Block block, OnTickEvent @event)
    {
        BreakIfCannotSurvive(block, @event.World, @event.X, @event.Y, @event.Z);

        if (@event.World.Lighting.GetBrightness(LightType.Block, @event.X, @event.Y + 1, @event.Z) < 9) return;

        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        if (meta >= 7) return;

        float moisture = GetAvailableMoisture(block, @event.World.Reader, @event.X, @event.Y, @event.Z);
        if (Random.Shared.Next(100) / moisture != 0) return;

        ++meta;
        @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, meta);
    }

    private static float GetAvailableMoisture(Block block, IBlockReader read, int x, int y, int z)
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
                if (blockBelow == Block.Farmland.id)
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

    // ── IBlockPhysics ─────────────────────────────────────────────

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event)
        => @event.World.Reader.GetBlockId(@event.X, @event.Y - 1, @event.Z) == Block.Farmland.id;

    public bool CanGrow(Block block, OnTickEvent ctx)
        => (ctx.World.Reader.GetBrightness(ctx.X, ctx.Y, ctx.Z) >= 8 || ctx.World.Lighting.HasSkyLight(ctx.X, ctx.Y, ctx.Z))
           && ctx.World.Reader.GetBlockId(ctx.X, ctx.Y - 1, ctx.Z) == Block.Farmland.id;

    public void NeighborUpdate(Block block, OnTickEvent @event)
        => BreakIfCannotSurvive(block, @event.World, @event.X, @event.Y, @event.Z);

    private static void BreakIfCannotSurvive(Block block, IWorldContext level, int x, int y, int z)
    {
        if (block.canGrow(new OnTickEvent(level, x, y, z, level.Reader.GetBlockMeta(x, y, z), level.Reader.GetBlockId(x, y, z)))) return;

        block.dropStacks(new OnDropEvent(level, x, y, z, level.Reader.GetBlockMeta(x, y, z)));
        level.Writer.SetBlock(x, y, z, 0);
    }

    // ── IBlockVisuals ─────────────────────────────────────────────

    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
        => defaultTexture + (meta < 0 ? 7 : meta);

    // ── IBlockLifecycle ───────────────────────────────────────────

    public int GetDroppedItemId(Block block, int blockMeta, int defaultItemId) => blockMeta == 7 ? Item.ByName("wheat").Id : -1;

    public void OnDropStacks(Block block, OnDropEvent @event)
    {
        if (@event.World.IsRemote || !@event.World.Rules.GetBool(DefaultRules.DoTileDrops)) return;

        for (int attempt = 0; attempt < 3; ++attempt)
        {
            if (Random.Shared.Next(15) > @event.Meta) continue;

            float offsetX = Random.Shared.NextSingle() * DropSpread + (1.0F - DropSpread) * 0.5F;
            float offsetY = Random.Shared.NextSingle() * DropSpread + (1.0F - DropSpread) * 0.5F;
            float offsetZ = Random.Shared.NextSingle() * DropSpread + (1.0F - DropSpread) * 0.5F;
            EntityItem entityItem = new(@event.World, @event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ, new ItemStack(Item.ByName("seeds")))
            {
                DelayBeforeCanPickup = 10
            };
            @event.World.Entities.SpawnEntity(entityItem);
        }
    }
}
