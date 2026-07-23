using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Physics, lifecycle, and visuals for track blocks (rail, powered rail, detector rail).
///     <c>isPoweredTrack</c> disables corner curving (straight/ramp shapes only, metadata 0-5)
///     and switches the texture/neighbor-update rules to the golden-rail variant. Detector rail
///     shares this instance for shape/placement rules but keeps its own <see cref="DetectorRailBehavior" />
///     for the actual minecart-detection redstone signal.
/// </summary>
public sealed class RailBehavior : IBlockPhysics, IBlockLifecycle, IBlockVisuals
{
    private readonly bool _isPoweredTrack;

    public RailBehavior(bool isPoweredTrack) => _isPoweredTrack = isPoweredTrack;

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (@event.World.IsRemote) return;
        UpdateShape(@event.World, @event.X, @event.Y, @event.Z, true);
        if (block.id != BlockRegistry.Get("powered_rail").id) return;
        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        NeighborUpdate(block, new OnTickEvent(@event.World, @event.X, @event.Y, @event.Z, meta, block.id));
    }

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        int meta = reader.GetBlockMeta(x, y, z);
        if (meta is >= 2 and <= 5)
        {
            block.SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 10.0F / 16.0F, 1.0F);
        }
        else
        {
            block.SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 2.0F / 16.0F, 1.0F);
        }
    }

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event)
        => @event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z);

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (@event.World.IsRemote) return;

        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        int railMeta = _isPoweredTrack ? meta & 7 : meta;

        bool shouldBreak = !@event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z) ||
                           railMeta == 2 && !@event.World.Reader.ShouldSuffocate(@event.X + 1, @event.Y, @event.Z) ||
                           railMeta == 3 && !@event.World.Reader.ShouldSuffocate(@event.X - 1, @event.Y, @event.Z) ||
                           railMeta == 4 && !@event.World.Reader.ShouldSuffocate(@event.X, @event.Y, @event.Z - 1) ||
                           railMeta == 5 && !@event.World.Reader.ShouldSuffocate(@event.X, @event.Y, @event.Z + 1);

        if (shouldBreak)
        {
            block.DropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)));
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        }
        else if (block.id == BlockRegistry.Get("powered_rail").id)
        {
            bool isPowered = @event.World.Redstone.IsPowered(@event.X, @event.Y, @event.Z) || @event.World.Redstone.IsPowered(@event.X, @event.Y + 1, @event.Z);
            isPowered = isPowered
                        || IsPoweredByConnectedRails(@event.World, @event.X, @event.Y, @event.Z, meta, true, 0)
                        || IsPoweredByConnectedRails(@event.World, @event.X, @event.Y, @event.Z, meta, false, 0);

            bool stateChanged = false;
            if (isPowered && (meta & 8) == 0)
            {
                @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, railMeta | 8);
                stateChanged = true;
            }
            else if (!isPowered && (meta & 8) != 0)
            {
                @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, railMeta);
                stateChanged = true;
            }

            if (!stateChanged) return;

            @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y, @event.Z, block.id);

            if (railMeta is 2 or 3 or 4 or 5) @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y + 1, @event.Z, block.id);

            @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y - 1, @event.Z, block.id);
        }
        else if (block.id > 0 &&
                 Block.Blocks[block.id].canEmitRedstonePower() &&
                 !_isPoweredTrack &&
                 new TrackLogic(@event.World, new Vec3i(@event.X, @event.Y, @event.Z)).GetAdjacentTracks() == 3)
        {
            UpdateShape(@event.World, @event.X, @event.Y, @event.Z, false);
        }
    }

    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
    {
        if (_isPoweredTrack)
        {
            if (block.id == BlockRegistry.Get("powered_rail").id && (meta & 8) == 0) return BlockTextures.PoweredRailOff;
        }
        else if (meta >= 6)
        {
            return BlockTextures.RailCorner;
        }

        return defaultTexture;
    }

    private static void UpdateShape(IWorldContext level, int x, int y, int z, bool force)
    {
        if (!level.IsRemote) new TrackLogic(level, new Vec3i(x, y, z)).UpdateState(level.Redstone.IsPowered(x, y, z), force);
    }

    private static bool IsPoweredByConnectedRails(IWorldContext level, int x, int y, int z, int meta, bool towardsNegative, int depth)
    {
        if (depth >= 8) return false;

        int shape = meta & 7;
        bool isSameY = true;
        switch (shape)
        {
            case 0:
                if (towardsNegative) ++z; else --z;
                break;
            case 1:
                if (towardsNegative) --x; else ++x;
                break;
            case 2:
                if (towardsNegative) { --x; }
                else { ++x; ++y; isSameY = false; }
                shape = 1;
                break;
            case 3:
                if (towardsNegative) { --x; ++y; isSameY = false; }
                else { ++x; }
                shape = 1;
                break;
            case 4:
                if (towardsNegative) { ++z; }
                else { --z; ++y; isSameY = false; }
                shape = 0;
                break;
            case 5:
                if (towardsNegative) { ++z; ++y; isSameY = false; }
                else { --z; }
                shape = 0;
                break;
        }

        return IsPoweredByRail(level, x, y, z, towardsNegative, depth, shape) ||
               (isSameY && IsPoweredByRail(level, x, y - 1, z, towardsNegative, depth, shape));
    }

    private static bool IsPoweredByRail(IWorldContext level, int x, int y, int z, bool towardsNegative, int depth, int shape)
    {
        int blockId = level.Reader.GetBlockId(x, y, z);
        if (blockId != BlockRegistry.Get("powered_rail").id) return false;

        int meta = level.Reader.GetBlockMeta(x, y, z);
        int railMeta = meta & 7;

        if (shape == 1 && railMeta is 0 or 4 or 5) return false;
        if (shape == 0 && railMeta is 1 or 2 or 3) return false;

        if ((meta & 8) == 0) return false;

        if (!level.Redstone.IsPowered(x, y, z) && !level.Redstone.IsPowered(x, y + 1, z))
        {
            return IsPoweredByConnectedRails(level, x, y, z, meta, towardsNegative, depth + 1);
        }

        return true;
    }

    public static bool IsRail(IWorldContext level, int x, int y, int z)
    {
        int blockId = level.Reader.GetBlockId(x, y, z);
        return IsRail(blockId);
    }

    public static bool IsRail(int blockId)
        => blockId == BlockRegistry.Get("rail").id || blockId == BlockRegistry.Get("powered_rail").id || blockId == BlockRegistry.Get("detector_rail").id;

    /// <summary>True for powered/detector rail: straight+ramp shapes only, no corners.</summary>
    public static bool IsAlwaysStraight(Block block) => block.Physics is RailBehavior { _isPoweredTrack: true };

    /// <summary>
    ///     Computes the metadata (0-9) representing which two neighbors a rail piece connects to,
    ///     propagating connection updates to adjacent track pieces exactly like vanilla's recursive
    ///     rail-shape recalculation.
    /// </summary>
    private sealed class TrackLogic
    {
        private readonly List<Vec3i> _connectedTracks = [];
        private readonly bool _isPoweredRail;
        private readonly IWorldContext _level;
        private readonly Vec3i _trackPos;

        public TrackLogic(IWorldContext level, Vec3i pos)
        {
            _level = level;
            _trackPos = pos;

            int blockId = level.Reader.GetBlockId(pos.X, pos.Y, pos.Z);
            int meta = level.Reader.GetBlockMeta(pos.X, pos.Y, pos.Z);

            Block candidate = Block.Blocks[blockId];
            if (candidate != null && IsAlwaysStraight(candidate))
            {
                _isPoweredRail = true;
                meta &= -9;
            }
            else
            {
                _isPoweredRail = false;
            }

            SetConnections(meta);
        }

        public void UpdateState(bool powered, bool forceUpdate)
        {
            bool north = AttemptConnectionAt(new Vec3i(_trackPos.X, _trackPos.Y, _trackPos.Z - 1));
            bool south = AttemptConnectionAt(new Vec3i(_trackPos.X, _trackPos.Y, _trackPos.Z + 1));
            bool west = AttemptConnectionAt(new Vec3i(_trackPos.X - 1, _trackPos.Y, _trackPos.Z));
            bool east = AttemptConnectionAt(new Vec3i(_trackPos.X + 1, _trackPos.Y, _trackPos.Z));

            int meta = -1;
            if ((north || south) && !west && !east) meta = 0;
            if ((west || east) && !north && !south) meta = 1;

            if (!_isPoweredRail)
            {
                if (south && east && !north && !west) meta = 6;
                if (south && west && !north && !east) meta = 7;
                if (north && west && !south && !east) meta = 8;
                if (north && east && !south && !west) meta = 9;
            }

            if (meta == -1)
            {
                if (north || south) meta = 0;
                if (west || east) meta = 1;

                if (!_isPoweredRail)
                {
                    if (powered)
                    {
                        if (south && east) meta = 6;
                        if (west && south) meta = 7;
                        if (east && north) meta = 9;
                        if (north && west) meta = 8;
                    }
                    else
                    {
                        if (north && west) meta = 8;
                        if (east && north) meta = 9;
                        if (west && south) meta = 7;
                        if (south && east) meta = 6;
                    }
                }
            }

            if (meta == 0)
            {
                if (IsRail(_level, _trackPos.X, _trackPos.Y + 1, _trackPos.Z - 1)) meta = 4;
                if (IsRail(_level, _trackPos.X, _trackPos.Y + 1, _trackPos.Z + 1)) meta = 5;
            }

            if (meta == 1)
            {
                if (IsRail(_level, _trackPos.X + 1, _trackPos.Y + 1, _trackPos.Z)) meta = 2;
                if (IsRail(_level, _trackPos.X - 1, _trackPos.Y + 1, _trackPos.Z)) meta = 3;
            }

            if (meta < 0) meta = 0;

            SetConnections(meta);

            int finalMeta = meta;
            if (_isPoweredRail)
            {
                finalMeta = _level.Reader.GetBlockMeta(_trackPos.X, _trackPos.Y, _trackPos.Z) & 8 | meta;
            }

            if (!forceUpdate && _level.Reader.GetBlockMeta(_trackPos.X, _trackPos.Y, _trackPos.Z) == finalMeta) return;
            _level.Writer.SetBlockMeta(_trackPos.X, _trackPos.Y, _trackPos.Z, finalMeta);
            foreach (Vec3i pos in _connectedTracks)
            {
                TrackLogic? logic = GetMinecartTrackLogic(pos);
                if (logic == null) continue;
                logic.RefreshConnectedTracks();
                if (logic.CanConnectTo(this))
                {
                    logic.ConnectTo(this);
                }
            }
        }

        public int GetAdjacentTracks()
        {
            int count = 0;
            if (IsMinecartTrack(new Vec3i(_trackPos.X, _trackPos.Y, _trackPos.Z - 1))) ++count;
            if (IsMinecartTrack(new Vec3i(_trackPos.X, _trackPos.Y, _trackPos.Z + 1))) ++count;
            if (IsMinecartTrack(new Vec3i(_trackPos.X - 1, _trackPos.Y, _trackPos.Z))) ++count;
            if (IsMinecartTrack(new Vec3i(_trackPos.X + 1, _trackPos.Y, _trackPos.Z))) ++count;
            return count;
        }

        private void SetConnections(int meta)
        {
            _connectedTracks.Clear();

            int trackX = _trackPos.X;
            int trackY = _trackPos.Y;
            int trackZ = _trackPos.Z;

            _connectedTracks.AddRange(meta switch
            {
                0 => [new Vec3i(trackX, trackY, trackZ - 1), new Vec3i(trackX, trackY, trackZ + 1)],
                1 => [new Vec3i(trackX - 1, trackY, trackZ), new Vec3i(trackX + 1, trackY, trackZ)],
                2 => [new Vec3i(trackX - 1, trackY, trackZ), new Vec3i(trackX + 1, trackY + 1, trackZ)],
                3 => [new Vec3i(trackX - 1, trackY + 1, trackZ), new Vec3i(trackX + 1, trackY, trackZ)],
                4 => [new Vec3i(trackX, trackY + 1, trackZ - 1), new Vec3i(trackX, trackY, trackZ + 1)],
                5 => [new Vec3i(trackX, trackY, trackZ - 1), new Vec3i(trackX, trackY + 1, trackZ + 1)],
                6 => [new Vec3i(trackX + 1, trackY, trackZ), new Vec3i(trackX, trackY, trackZ + 1)],
                7 => [new Vec3i(trackX - 1, trackY, trackZ), new Vec3i(trackX, trackY, trackZ + 1)],
                8 => [new Vec3i(trackX - 1, trackY, trackZ), new Vec3i(trackX, trackY, trackZ - 1)],
                9 => [new Vec3i(trackX + 1, trackY, trackZ), new Vec3i(trackX, trackY, trackZ - 1)],
                _ => []
            });
        }

        private void RefreshConnectedTracks()
        {
            for (int i = _connectedTracks.Count - 1; i >= 0; i--)
            {
                Vec3i pos = _connectedTracks[i];
                TrackLogic? logic = GetMinecartTrackLogic(pos);

                if (logic != null && logic.IsConnectedTo(this))
                {
                    _connectedTracks[i] = new Vec3i(logic._trackPos.X, logic._trackPos.Y, logic._trackPos.Z);
                }
                else
                {
                    _connectedTracks.RemoveAt(i);
                }
            }
        }

        private bool IsMinecartTrack(Vec3i pos) =>
            IsRail(_level, pos.X, pos.Y, pos.Z) ||
            IsRail(_level, pos.X, pos.Y + 1, pos.Z) ||
            IsRail(_level, pos.X, pos.Y - 1, pos.Z);

        private TrackLogic? GetMinecartTrackLogic(Vec3i pos)
        {
            if (IsRail(_level, pos.X, pos.Y, pos.Z)) return new TrackLogic(_level, pos);
            if (IsRail(_level, pos.X, pos.Y + 1, pos.Z)) return new TrackLogic(_level, new Vec3i(pos.X, pos.Y + 1, pos.Z));
            if (IsRail(_level, pos.X, pos.Y - 1, pos.Z)) return new TrackLogic(_level, new Vec3i(pos.X, pos.Y - 1, pos.Z));
            return null;
        }

        private bool IsConnectedTo(TrackLogic targetLogic)
        {
            foreach (Vec3i pos in _connectedTracks)
            {
                if (pos.X == targetLogic._trackPos.X && pos.Z == targetLogic._trackPos.Z)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsInTrack(Vec3i pos)
        {
            foreach (Vec3i connectedPos in _connectedTracks)
            {
                if (connectedPos.X == pos.X && connectedPos.Z == pos.Z)
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanConnectTo(TrackLogic targetLogic)
        {
            if (IsConnectedTo(targetLogic)) return true;
            if (_connectedTracks.Count == 2) return false;
            if (_connectedTracks.Count == 0) return true;

            // This logic originally returned true regardless of the condition in decompiled source.
            // It's a known Beta 1.7.3 quirk. Kept original behavior but cleaned up.
            return true;
        }

        private void ConnectTo(TrackLogic targetLogic)
        {
            _connectedTracks.Add(new Vec3i(targetLogic._trackPos.X, targetLogic._trackPos.Y, targetLogic._trackPos.Z));

            bool north = IsInTrack(new Vec3i(_trackPos.X, _trackPos.Y, _trackPos.Z - 1));
            bool south = IsInTrack(new Vec3i(_trackPos.X, _trackPos.Y, _trackPos.Z + 1));
            bool west = IsInTrack(new Vec3i(_trackPos.X - 1, _trackPos.Y, _trackPos.Z));
            bool east = IsInTrack(new Vec3i(_trackPos.X + 1, _trackPos.Y, _trackPos.Z));

            int meta = -1;
            if (north || south) meta = 0;
            if (west || east) meta = 1;

            if (!_isPoweredRail)
            {
                if (south && east && !north && !west) meta = 6;
                if (south && west && !north && !east) meta = 7;
                if (north && west && !south && !east) meta = 8;
                if (north && east && !south && !west) meta = 9;
            }

            if (meta == 0)
            {
                if (IsRail(_level, _trackPos.X, _trackPos.Y + 1, _trackPos.Z - 1)) meta = 4;
                if (IsRail(_level, _trackPos.X, _trackPos.Y + 1, _trackPos.Z + 1)) meta = 5;
            }

            if (meta == 1)
            {
                if (IsRail(_level, _trackPos.X + 1, _trackPos.Y + 1, _trackPos.Z)) meta = 2;
                if (IsRail(_level, _trackPos.X - 1, _trackPos.Y + 1, _trackPos.Z)) meta = 3;
            }

            if (meta < 0) meta = 0;

            int finalMeta = meta;
            if (_isPoweredRail)
            {
                finalMeta = _level.Reader.GetBlockMeta(_trackPos.X, _trackPos.Y, _trackPos.Z) & 8 | meta;
            }

            _level.Writer.SetBlockMeta(_trackPos.X, _trackPos.Y, _trackPos.Z, finalMeta);
        }

        private bool AttemptConnectionAt(Vec3i pos)
        {
            TrackLogic? logic = GetMinecartTrackLogic(pos);
            if (logic == null) return false;

            logic.RefreshConnectedTracks();
            return logic.CanConnectTo(this);
        }
    }
}
