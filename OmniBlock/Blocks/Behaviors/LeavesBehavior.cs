using OmniBlock.Items;
using OmniBlock.Worlds.Colors;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Leaves: distance-to-trunk decay (breadth-first flood fill capped at radius 4, re-derived from
///     scratch every check since no per-block decay-distance cache persists across ticks), sapling
///     drop chance, harvest-tool silk-touch drop, and the fancy/fast graphics opacity toggle. The
///     toggle is process-wide (there is only one leaves block in Beta 1.7.3), so
///     <see cref="SetGraphicsLevel" /> mutates shared state on this singleton rather than
///     per-<see cref="Block" /> instance state.
///     <para>
///         Trunk block, sapling drop, and harvest tool are all required, (see <c>BehaviorRegistry</c>'s <c>"leaves"</c>
///         entry).
///         Resolved eagerly, not lazily: every <see cref="Block" /> already exists by the time any
///         behavior factory runs (pass 2 of <c>Blocks.LoadAndBuild</c> starts only after
///         pass 1 finishes constructing all of them). "Same-species leaves" checks compare against
///         the owning <see cref="Block" /> passed into each call, not a separate cached id.
///     </para>
/// </summary>
public sealed class LeavesBehavior(Block trunk, Block saplingItem, Item harvestToolItem, int[] fancyTextures, int[] fastTextures) : BlockRuntimeBehavior, IBlockTicker, IBlockLifecycle, IBlockVisuals
{
    private const sbyte DecayRadius = 4;
    private const sbyte RegionSize = 32;
    private const int LoadCheckExtent = DecayRadius + 1;
    private const int PlaneSize = RegionSize * RegionSize;
    private const int CenterOffset = RegionSize / 2;

    private readonly ThreadLocal<int[]?> _decayRegion = new(() => null);
    private bool _graphicsLevel;

    public void OnBreak(Block block, OnBreakEvent @event)
    {
        const sbyte searchRadius = 1;
        const int loadCheckExtent = searchRadius + 1;
        if (!@event.World.ChunkHost.IsRegionLoaded(@event.X - loadCheckExtent, @event.Y - loadCheckExtent, @event.Z - loadCheckExtent, @event.X + loadCheckExtent, @event.Y + loadCheckExtent, @event.Z + loadCheckExtent)) return;

        for (var offsetX = -searchRadius; offsetX <= searchRadius; ++offsetX)
        for (var offsetY = -searchRadius; offsetY <= searchRadius; ++offsetY)
        for (var offsetZ = -searchRadius; offsetZ <= searchRadius; ++offsetZ)
        {
            var blockId = @event.World.Reader.GetBlockId(@event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ);
            if (blockId != block.Id) continue;

            var leavesMeta = @event.World.Reader.GetBlockMeta(@event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ);
            @event.World.Writer.SetBlockMetaWithoutNotifyingNeighbors(@event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ, leavesMeta | 8);
        }
    }

    public void OnAfterBreak(Block block, OnAfterBreakEvent ctx)
    {
        var hand = ctx.Player.GetHand();
        if (ctx.World.IsRemote || hand == null || hand.ItemId != harvestToolItem.Id) return;

        ctx.Player.IncreaseStat(Stats.Stats.MineBlockStatArray[block.Id], 1);
        Block.DropStack(ctx.World, ctx.X, ctx.Y, ctx.Z, new ItemStack(block.Id, 1, ctx.Meta & 3));
    }

    public int GetDroppedItemCount(Block block, int defaultCount)
    {
        return Random.Shared.Next(20) == 0 ? 1 : 0;
    }

    public int GetDroppedItemId(Block block, int blockMeta, int defaultItemId)
    {
        return saplingItem.Id;
    }

    public (int primaryMeta, int backupItemId, int backupMeta) GetPickBlockItem(Block block, int blockMeta, int defaultBackupId, int defaultBackupMeta)
    {
        return (blockMeta & 3, saplingItem.Id, blockMeta & 3);
    }

    public void OnTick(Block block, OnTickEvent @event)
    {
        if (@event.World.IsRemote) return;
        var meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        if ((meta & 8) == 0) return;
        _decayRegion.Value ??= new int[RegionSize * RegionSize * RegionSize];

        var decayRegion = _decayRegion.Value;
        var trunkId = trunk.Id;

        int distanceToLog;
        if (@event.World.ChunkHost.IsRegionLoaded(@event.X - LoadCheckExtent, @event.Y - LoadCheckExtent, @event.Z - LoadCheckExtent, @event.X + LoadCheckExtent, @event.Y + LoadCheckExtent, @event.Z + LoadCheckExtent))
        {
            distanceToLog = -DecayRadius;

            while (distanceToLog <= DecayRadius)
            {
                for (var dx = -DecayRadius; dx <= DecayRadius; ++dx)
                for (var dy = -DecayRadius; dy <= DecayRadius; ++dy)
                {
                    var blockId = @event.World.Reader.GetBlockId(@event.X + distanceToLog, @event.Y + dx, @event.Z + dy);
                    if (blockId == trunkId)
                        decayRegion[(distanceToLog + CenterOffset) * PlaneSize + (dx + CenterOffset) * RegionSize + dy + CenterOffset] = 0;
                    else if (blockId == block.Id)
                        decayRegion[(distanceToLog + CenterOffset) * PlaneSize + (dx + CenterOffset) * RegionSize + dy + CenterOffset] = -2;
                    else
                        decayRegion[(distanceToLog + CenterOffset) * PlaneSize + (dx + CenterOffset) * RegionSize + dy + CenterOffset] = -1;
                }

                ++distanceToLog;
            }

            for (distanceToLog = 1; distanceToLog <= 4; ++distanceToLog)
            for (var dx = -DecayRadius; dx <= DecayRadius; ++dx)
            for (var dy = -DecayRadius; dy <= DecayRadius; ++dy)
            for (var dz = -DecayRadius; dz <= DecayRadius; ++dz)
            {
                if (decayRegion[(dx + CenterOffset) * PlaneSize + (dy + CenterOffset) * RegionSize + dz + CenterOffset] != distanceToLog - 1) continue;

                if (decayRegion[(dx + CenterOffset - 1) * PlaneSize + (dy + CenterOffset) * RegionSize + dz + CenterOffset] == -2)
                    decayRegion[(dx + CenterOffset - 1) * PlaneSize + (dy + CenterOffset) * RegionSize + dz + CenterOffset] = distanceToLog;

                if (decayRegion[(dx + CenterOffset + 1) * PlaneSize + (dy + CenterOffset) * RegionSize + dz + CenterOffset] == -2)
                    decayRegion[(dx + CenterOffset + 1) * PlaneSize + (dy + CenterOffset) * RegionSize + dz + CenterOffset] = distanceToLog;

                if (decayRegion[(dx + CenterOffset) * PlaneSize + (dy + CenterOffset - 1) * RegionSize + dz + CenterOffset] == -2)
                    decayRegion[(dx + CenterOffset) * PlaneSize + (dy + CenterOffset - 1) * RegionSize + dz + CenterOffset] = distanceToLog;

                if (decayRegion[(dx + CenterOffset) * PlaneSize + (dy + CenterOffset + 1) * RegionSize + dz + CenterOffset] == -2)
                    decayRegion[(dx + CenterOffset) * PlaneSize + (dy + CenterOffset + 1) * RegionSize + dz + CenterOffset] = distanceToLog;

                if (decayRegion[(dx + CenterOffset) * PlaneSize + (dy + CenterOffset) * RegionSize + (dz + CenterOffset - 1)] == -2)
                    decayRegion[(dx + CenterOffset) * PlaneSize + (dy + CenterOffset) * RegionSize + (dz + CenterOffset - 1)] = distanceToLog;

                if (decayRegion[(dx + CenterOffset) * PlaneSize + (dy + CenterOffset) * RegionSize + dz + CenterOffset + 1] == -2)
                    decayRegion[(dx + CenterOffset) * PlaneSize + (dy + CenterOffset) * RegionSize + dz + CenterOffset + 1] = distanceToLog;
            }
        }

        distanceToLog = decayRegion[CenterOffset * PlaneSize + CenterOffset * RegionSize + CenterOffset];
        if (distanceToLog >= 0)
            @event.World.Writer.SetBlockMetaWithoutNotifyingNeighbors(@event.X, @event.Y, @event.Z, meta & -9);
        else
            BreakLeaves(block, @event.World, @event.X, @event.Y, @event.Z);
    }

    public int GetColor(Block block, int meta, int defaultColor)
    {
        return (meta & 1) == 1 ? FoliageColors.getSpruceColor() : (meta & 2) == 2 ? FoliageColors.getBirchColor() : FoliageColors.getDefaultColor();
    }

    public int GetColorMultiplier(Block block, IBlockReader reader, int x, int y, int z, int defaultColor)
    {
        var meta = reader.GetBlockMeta(x, y, z);
        if ((meta & 1) == 1) return FoliageColors.getSpruceColor();
        if ((meta & 2) == 2) return FoliageColors.getBirchColor();
        reader.GetBiomeSource().GetBiomesInArea(x, z, 1, 1);
        var temperature = reader.GetBiomeSource().TemperatureMap[0];
        var downfall = reader.GetBiomeSource().DownfallMap[0];
        return FoliageColors.getFoliageColor(temperature, downfall);
    }

    // Four species slots for two metadata bits, the same as the log the canopy grew from.
    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
    {
        return (_graphicsLevel ? fancyTextures : fastTextures)[meta & 3];
    }

    public bool IsSideVisible(Block block, IBlockReader reader, int x, int y, int z, Side side, bool defaultVisibility)
    {
        return (_graphicsLevel || reader.GetBlockId(x, y, z) != block.Id) && defaultVisibility;
    }

    public bool IsOpaque(Block block, bool defaultOpaque)
    {
        return !_graphicsLevel;
    }

    private static void BreakLeaves(Block block, IWorldContext level, int x, int y, int z)
    {
        block.DropStacks(new OnDropEvent(level, x, y, z, level.Reader.GetBlockMeta(x, y, z)));
        level.Writer.SetBlock(x, y, z, 0);
    }

    /// <summary>Toggles fancy (translucent) vs fast (opaque) leaves rendering.</summary>
    public static void SetGraphicsLevel(Block block, bool fancy)
    {
        if (block.Visuals is not LeavesBehavior behavior) return;
        behavior._graphicsLevel = fancy;
    }
}