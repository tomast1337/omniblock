using BetaSharp.Items;
using BetaSharp.Worlds.Colors;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Leaves: distance-to-trunk decay (breadth-first flood fill capped at radius 4, re-derived from
///     scratch every check since no per-block decay-distance cache persists across ticks), sapling
///     drop chance, harvest-tool silk-touch drop, and the fancy/fast graphics opacity toggle. The
///     toggle is process-wide (there is only one leaves block in Beta 1.7.3), so
///     <see cref="SetGraphicsLevel" /> mutates shared state on this singleton rather than
///     per-<see cref="Block" /> instance state.
///     <para>
///         Trunk block, sapling drop, and harvest tool are all required, JSON-declared per variant
///         (see <c>BehaviorRegistry</c>'s <c>"leaves"</c> entry) — no built-in vanilla fallback; an
///         omitted or unknown name throws immediately at startup rather than silently defaulting.
///         Resolved eagerly, not lazily: every <see cref="Block" /> already exists by the time any
///         behavior factory runs (pass 2 of <c>BlockRegistry.LoadAndBuild</c> starts only after
///         pass 1 finishes constructing all of them). "Same-species leaves" checks compare against
///         the owning <see cref="Block" /> passed into each call, not a separate cached id — a
///         leaves block is always its own same-species reference.
///     </para>
/// </summary>
public sealed class LeavesBehavior(Block trunk, int saplingItemId, int harvestToolItemId) : IBlockTicker, IBlockLifecycle, IBlockVisuals
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
        if (!@event.World.ChunkHost.IsRegionLoaded(@event.X - loadCheckExtent, @event.Y - loadCheckExtent, @event.Z - loadCheckExtent, @event.X + loadCheckExtent, @event.Y + loadCheckExtent, @event.Z + loadCheckExtent))
        {
            return;
        }

        for (int offsetX = -searchRadius; offsetX <= searchRadius; ++offsetX)
        {
            for (int offsetY = -searchRadius; offsetY <= searchRadius; ++offsetY)
            {
                for (int offsetZ = -searchRadius; offsetZ <= searchRadius; ++offsetZ)
                {
                    int blockId = @event.World.Reader.GetBlockId(@event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ);
                    if (blockId != block.id)
                    {
                        continue;
                    }

                    int leavesMeta = @event.World.Reader.GetBlockMeta(@event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ);
                    @event.World.Writer.SetBlockMetaWithoutNotifyingNeighbors(@event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ, leavesMeta | 8);
                }
            }
        }
    }

    public void OnAfterBreak(Block block, OnAfterBreakEvent ctx)
    {
        ItemStack? hand = ctx.Player.GetHand();
        if (ctx.World.IsRemote || hand == null || hand.ItemId != harvestToolItemId) return;

        ctx.Player.IncreaseStat(Stats.Stats.MineBlockStatArray[block.id], 1);
        Block.DropStack(ctx.World, ctx.X, ctx.Y, ctx.Z, new ItemStack(block.id, 1, ctx.Meta & 3));
    }

    public int GetDroppedItemCount(Block block, int defaultCount) => Random.Shared.Next(20) == 0 ? 1 : 0;

    public int GetDroppedItemId(Block block, int blockMeta, int defaultItemId) => saplingItemId;

    public void OnTick(Block block, OnTickEvent @event)
    {
        if (@event.World.IsRemote) return;
        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        if ((meta & 8) == 0) return;
        _decayRegion.Value ??= new int[RegionSize * RegionSize * RegionSize];

        int[] decayRegion = _decayRegion.Value;
        int trunkId = trunk.id;

        int distanceToLog;
        if (@event.World.ChunkHost.IsRegionLoaded(@event.X - LoadCheckExtent, @event.Y - LoadCheckExtent, @event.Z - LoadCheckExtent, @event.X + LoadCheckExtent, @event.Y + LoadCheckExtent, @event.Z + LoadCheckExtent))
        {
            distanceToLog = -DecayRadius;

            while (distanceToLog <= DecayRadius)
            {
                for (int dx = -DecayRadius; dx <= DecayRadius; ++dx)
                {
                    for (int dy = -DecayRadius; dy <= DecayRadius; ++dy)
                    {
                        int blockId = @event.World.Reader.GetBlockId(@event.X + distanceToLog, @event.Y + dx, @event.Z + dy);
                        if (blockId == trunkId)
                        {
                            decayRegion[(distanceToLog + CenterOffset) * PlaneSize + (dx + CenterOffset) * RegionSize + dy + CenterOffset] = 0;
                        }
                        else if (blockId == block.id)
                        {
                            decayRegion[(distanceToLog + CenterOffset) * PlaneSize + (dx + CenterOffset) * RegionSize + dy + CenterOffset] = -2;
                        }
                        else
                        {
                            decayRegion[(distanceToLog + CenterOffset) * PlaneSize + (dx + CenterOffset) * RegionSize + dy + CenterOffset] = -1;
                        }
                    }
                }

                ++distanceToLog;
            }

            for (distanceToLog = 1; distanceToLog <= 4; ++distanceToLog)
            {
                for (int dx = -DecayRadius; dx <= DecayRadius; ++dx)
                {
                    for (int dy = -DecayRadius; dy <= DecayRadius; ++dy)
                    {
                        for (int dz = -DecayRadius; dz <= DecayRadius; ++dz)
                        {
                            if (decayRegion[(dx + CenterOffset) * PlaneSize + (dy + CenterOffset) * RegionSize + dz + CenterOffset] != distanceToLog - 1)
                            {
                                continue;
                            }

                            if (decayRegion[(dx + CenterOffset - 1) * PlaneSize + (dy + CenterOffset) * RegionSize + dz + CenterOffset] == -2)
                            {
                                decayRegion[(dx + CenterOffset - 1) * PlaneSize + (dy + CenterOffset) * RegionSize + dz + CenterOffset] = distanceToLog;
                            }

                            if (decayRegion[(dx + CenterOffset + 1) * PlaneSize + (dy + CenterOffset) * RegionSize + dz + CenterOffset] == -2)
                            {
                                decayRegion[(dx + CenterOffset + 1) * PlaneSize + (dy + CenterOffset) * RegionSize + dz + CenterOffset] = distanceToLog;
                            }

                            if (decayRegion[(dx + CenterOffset) * PlaneSize + (dy + CenterOffset - 1) * RegionSize + dz + CenterOffset] == -2)
                            {
                                decayRegion[(dx + CenterOffset) * PlaneSize + (dy + CenterOffset - 1) * RegionSize + dz + CenterOffset] = distanceToLog;
                            }

                            if (decayRegion[(dx + CenterOffset) * PlaneSize + (dy + CenterOffset + 1) * RegionSize + dz + CenterOffset] == -2)
                            {
                                decayRegion[(dx + CenterOffset) * PlaneSize + (dy + CenterOffset + 1) * RegionSize + dz + CenterOffset] = distanceToLog;
                            }

                            if (decayRegion[(dx + CenterOffset) * PlaneSize + (dy + CenterOffset) * RegionSize + (dz + CenterOffset - 1)] == -2)
                            {
                                decayRegion[(dx + CenterOffset) * PlaneSize + (dy + CenterOffset) * RegionSize + (dz + CenterOffset - 1)] = distanceToLog;
                            }

                            if (decayRegion[(dx + CenterOffset) * PlaneSize + (dy + CenterOffset) * RegionSize + dz + CenterOffset + 1] == -2)
                            {
                                decayRegion[(dx + CenterOffset) * PlaneSize + (dy + CenterOffset) * RegionSize + dz + CenterOffset + 1] = distanceToLog;
                            }
                        }
                    }
                }
            }
        }

        distanceToLog = decayRegion[CenterOffset * PlaneSize + CenterOffset * RegionSize + CenterOffset];
        if (distanceToLog >= 0)
        {
            @event.World.Writer.SetBlockMetaWithoutNotifyingNeighbors(@event.X, @event.Y, @event.Z, meta & -9);
        }
        else
        {
            BreakLeaves(block, @event.World, @event.X, @event.Y, @event.Z);
        }
    }

    public int GetColor(Block block, int meta, int defaultColor)
        => (meta & 1) == 1 ? FoliageColors.getSpruceColor() : (meta & 2) == 2 ? FoliageColors.getBirchColor() : FoliageColors.getDefaultColor();

    public int GetColorMultiplier(Block block, IBlockReader reader, int x, int y, int z, int defaultColor)
    {
        int meta = reader.GetBlockMeta(x, y, z);
        if ((meta & 1) == 1) return FoliageColors.getSpruceColor();
        if ((meta & 2) == 2) return FoliageColors.getBirchColor();
        reader.GetBiomeSource().GetBiomesInArea(x, z, 1, 1);
        double temperature = reader.GetBiomeSource().TemperatureMap[0];
        double downfall = reader.GetBiomeSource().DownfallMap[0];
        return FoliageColors.getFoliageColor(temperature, downfall);
    }

    public int GetTexture(Block block, Side side, int meta, int defaultTexture) => (meta & 3) == 1 ? defaultTexture + 80 : defaultTexture;

    public bool IsSideVisible(Block block, IBlockReader reader, int x, int y, int z, Side side, bool defaultVisibility)
        => (_graphicsLevel || reader.GetBlockId(x, y, z) != block.id) && defaultVisibility;

    public bool IsOpaque(Block block, bool defaultOpaque) => !_graphicsLevel;

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
        block.TextureId = BlockTextures.LeavesOak + (fancy ? 0 : 1);
    }
}
