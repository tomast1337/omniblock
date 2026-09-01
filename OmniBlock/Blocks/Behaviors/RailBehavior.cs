using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Physics, lifecycle, and visuals for track blocks (rail, powered rail, detector rail).
///     <c>isPoweredTrack</c> disables corner curving (straight/ramp shapes only, metadata 0-5)
///     and switches the texture/neighbor-update rules to the golden-rail variant. Detector rail
///     shares this instance for shape/placement rules but keeps its own <see cref="DetectorRailBehavior" />
///     for the actual minecart-detection redstone signal.
/// </summary>
public sealed class RailBehavior(bool isPoweredTrack, int turn, int unpowered) : BlockRuntimeBehavior, IBlockPhysics, IBlockLifecycle, IBlockVisuals
{
    private readonly bool _isPoweredTrack = isPoweredTrack;

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (@event.World.IsRemote) return;
        UpdateShape(@event.World, @event.X, @event.Y, @event.Z, true);
        if (block.Id != Blocks.Get("powered_rail").Id) return;
        var meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        NeighborUpdate(block, new OnTickEvent(@event.World, @event.X, @event.Y, @event.Z, meta, block.Id));
    }

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        var meta = reader.GetBlockMeta(x, y, z);
        if (meta is >= 2 and <= 5)
            block.SetRuntimeBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 10.0F / 16.0F, 1.0F);
        else
            block.SetRuntimeBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 2.0F / 16.0F, 1.0F);
    }

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event)
    {
        return @event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z);
    }

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (@event.World.IsRemote) return;

        var meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        var railMeta = _isPoweredTrack ? meta & 7 : meta;

        var shouldBreak = !@event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z) ||
                          (railMeta == 2 && !@event.World.Reader.ShouldSuffocate(@event.X + 1, @event.Y, @event.Z)) ||
                          (railMeta == 3 && !@event.World.Reader.ShouldSuffocate(@event.X - 1, @event.Y, @event.Z)) ||
                          (railMeta == 4 && !@event.World.Reader.ShouldSuffocate(@event.X, @event.Y, @event.Z - 1)) ||
                          (railMeta == 5 && !@event.World.Reader.ShouldSuffocate(@event.X, @event.Y, @event.Z + 1));

        if (shouldBreak)
        {
            block.DropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)));
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        }
        else if (block.Id == Blocks.Get("powered_rail").Id)
        {
            var isPowered = @event.World.Redstone.IsPowered(@event.X, @event.Y, @event.Z) || @event.World.Redstone.IsPowered(@event.X, @event.Y + 1, @event.Z);
            isPowered = isPowered
                        || IsPoweredByConnectedRails(@event.World, @event.X, @event.Y, @event.Z, meta, true, 0)
                        || IsPoweredByConnectedRails(@event.World, @event.X, @event.Y, @event.Z, meta, false, 0);

            var stateChanged = false;
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

            @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y, @event.Z, block.Id);

            if (railMeta is 2 or 3 or 4 or 5) @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y + 1, @event.Z, block.Id);

            @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y - 1, @event.Z, block.Id);
        }
        else if (block.Id > 0 &&
                 Blocks.GetByProtocolId(block.Id).CanEmitRedstonePower() &&
                 !_isPoweredTrack &&
                 new TrackLogic(Blocks, @event.World, new Vec3I(@event.X, @event.Y, @event.Z)).GetAdjacentTracks() == 3)
        {
            UpdateShape(@event.World, @event.X, @event.Y, @event.Z, false);
        }
    }

    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
    {
        if (_isPoweredTrack)
        {
            if (block.Id == Blocks.Get("powered_rail").Id && (meta & 8) == 0) return unpowered;
        }
        else if (meta >= 6)
        {
            return turn;
        }

        return defaultTexture;
    }

    private void UpdateShape(IWorldContext level, int x, int y, int z, bool force)
    {
        if (!level.IsRemote) new TrackLogic(Blocks, level, new Vec3I(x, y, z)).UpdateState(level.Redstone.IsPowered(x, y, z), force);
    }

    private bool IsPoweredByConnectedRails(IWorldContext level, int x, int y, int z, int meta, bool towardsNegative, int depth)
    {
        if (depth >= 8) return false;

        var shape = meta & 7;
        var isSameY = true;
        switch (shape)
        {
            case 0:
                if (towardsNegative) ++z;
                else --z;
                break;
            case 1:
                if (towardsNegative) --x;
                else ++x;
                break;
            case 2:
                if (towardsNegative)
                {
                    --x;
                }
                else
                {
                    ++x;
                    ++y;
                    isSameY = false;
                }

                shape = 1;
                break;
            case 3:
                if (towardsNegative)
                {
                    --x;
                    ++y;
                    isSameY = false;
                }
                else
                {
                    ++x;
                }

                shape = 1;
                break;
            case 4:
                if (towardsNegative)
                {
                    ++z;
                }
                else
                {
                    --z;
                    ++y;
                    isSameY = false;
                }

                shape = 0;
                break;
            case 5:
                if (towardsNegative)
                {
                    ++z;
                    ++y;
                    isSameY = false;
                }
                else
                {
                    --z;
                }

                shape = 0;
                break;
        }

        return IsPoweredByRail(level, x, y, z, towardsNegative, depth, shape) ||
               (isSameY && IsPoweredByRail(level, x, y - 1, z, towardsNegative, depth, shape));
    }

    private bool IsPoweredByRail(IWorldContext level, int x, int y, int z, bool towardsNegative, int depth, int shape)
    {
        var blockId = level.Reader.GetBlockId(x, y, z);
        if (blockId != Blocks.Get("powered_rail").Id) return false;

        var meta = level.Reader.GetBlockMeta(x, y, z);
        var railMeta = meta & 7;

        if (shape == 1 && railMeta is 0 or 4 or 5) return false;
        if (shape == 0 && railMeta is 1 or 2 or 3) return false;

        if ((meta & 8) == 0) return false;

        if (!level.Redstone.IsPowered(x, y, z) && !level.Redstone.IsPowered(x, y + 1, z)) return IsPoweredByConnectedRails(level, x, y, z, meta, towardsNegative, depth + 1);

        return true;
    }

    public static bool IsRail(Block block)
    {
        return block.Physics is RailBehavior;
    }

    /// <summary>True for powered/detector rail: straight+ramp shapes only, no corners.</summary>
    public static bool IsAlwaysStraight(Block block)
    {
        return block.Physics is RailBehavior { _isPoweredTrack: true };
    }

    /// <summary>
    ///     Computes the metadata (0-9) representing which two neighbors a rail piece connects to,
    ///     propagating connection updates to adjacent track pieces exactly like vanilla's recursive
    ///     rail-shape recalculation.
    /// </summary>
    private sealed class TrackLogic
    {
        private readonly IBlockRuntimeView _blocks;
        private readonly List<Vec3I> _connectedTracks = [];
        private readonly bool _isPoweredRail;
        private readonly IWorldContext _level;
        private readonly Vec3I _trackPos;

        public TrackLogic(IBlockRuntimeView blocks, IWorldContext level, Vec3I pos)
        {
            _blocks = blocks;
            _level = level;
            _trackPos = pos;

            var blockId = level.Reader.GetBlockId(pos.X, pos.Y, pos.Z);
            var meta = level.Reader.GetBlockMeta(pos.X, pos.Y, pos.Z);

            if (_blocks.TryGetByProtocolId(blockId, out var candidate)
                && IsAlwaysStraight(candidate))
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
            var north = AttemptConnectionAt(new Vec3I(_trackPos.X, _trackPos.Y, _trackPos.Z - 1));
            var south = AttemptConnectionAt(new Vec3I(_trackPos.X, _trackPos.Y, _trackPos.Z + 1));
            var west = AttemptConnectionAt(new Vec3I(_trackPos.X - 1, _trackPos.Y, _trackPos.Z));
            var east = AttemptConnectionAt(new Vec3I(_trackPos.X + 1, _trackPos.Y, _trackPos.Z));

            var meta = -1;
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

            var finalMeta = meta;
            if (_isPoweredRail) finalMeta = (_level.Reader.GetBlockMeta(_trackPos.X, _trackPos.Y, _trackPos.Z) & 8) | meta;

            if (!forceUpdate && _level.Reader.GetBlockMeta(_trackPos.X, _trackPos.Y, _trackPos.Z) == finalMeta) return;
            _level.Writer.SetBlockMeta(_trackPos.X, _trackPos.Y, _trackPos.Z, finalMeta);
            foreach (var pos in _connectedTracks)
            {
                var logic = GetMinecartTrackLogic(pos);
                if (logic == null) continue;
                logic.RefreshConnectedTracks();
                if (logic.CanConnectTo(this)) logic.ConnectTo(this);
            }
        }

        public int GetAdjacentTracks()
        {
            var count = 0;
            if (IsMinecartTrack(new Vec3I(_trackPos.X, _trackPos.Y, _trackPos.Z - 1))) ++count;
            if (IsMinecartTrack(new Vec3I(_trackPos.X, _trackPos.Y, _trackPos.Z + 1))) ++count;
            if (IsMinecartTrack(new Vec3I(_trackPos.X - 1, _trackPos.Y, _trackPos.Z))) ++count;
            if (IsMinecartTrack(new Vec3I(_trackPos.X + 1, _trackPos.Y, _trackPos.Z))) ++count;
            return count;
        }

        private void SetConnections(int meta)
        {
            _connectedTracks.Clear();

            var trackX = _trackPos.X;
            var trackY = _trackPos.Y;
            var trackZ = _trackPos.Z;

            _connectedTracks.AddRange(meta switch
            {
                0 => [new Vec3I(trackX, trackY, trackZ - 1), new Vec3I(trackX, trackY, trackZ + 1)],
                1 => [new Vec3I(trackX - 1, trackY, trackZ), new Vec3I(trackX + 1, trackY, trackZ)],
                2 => [new Vec3I(trackX - 1, trackY, trackZ), new Vec3I(trackX + 1, trackY + 1, trackZ)],
                3 => [new Vec3I(trackX - 1, trackY + 1, trackZ), new Vec3I(trackX + 1, trackY, trackZ)],
                4 => [new Vec3I(trackX, trackY + 1, trackZ - 1), new Vec3I(trackX, trackY, trackZ + 1)],
                5 => [new Vec3I(trackX, trackY, trackZ - 1), new Vec3I(trackX, trackY + 1, trackZ + 1)],
                6 => [new Vec3I(trackX + 1, trackY, trackZ), new Vec3I(trackX, trackY, trackZ + 1)],
                7 => [new Vec3I(trackX - 1, trackY, trackZ), new Vec3I(trackX, trackY, trackZ + 1)],
                8 => [new Vec3I(trackX - 1, trackY, trackZ), new Vec3I(trackX, trackY, trackZ - 1)],
                9 => [new Vec3I(trackX + 1, trackY, trackZ), new Vec3I(trackX, trackY, trackZ - 1)],
                _ => []
            });
        }

        private void RefreshConnectedTracks()
        {
            for (var i = _connectedTracks.Count - 1; i >= 0; i--)
            {
                var pos = _connectedTracks[i];
                var logic = GetMinecartTrackLogic(pos);

                if (logic != null && logic.IsConnectedTo(this))
                    _connectedTracks[i] = new Vec3I(logic._trackPos.X, logic._trackPos.Y, logic._trackPos.Z);
                else
                    _connectedTracks.RemoveAt(i);
            }
        }

        private bool IsMinecartTrack(Vec3I pos)
        {
            return IsRail(_level, pos.X, pos.Y, pos.Z) ||
                   IsRail(_level, pos.X, pos.Y + 1, pos.Z) ||
                   IsRail(_level, pos.X, pos.Y - 1, pos.Z);
        }

        private TrackLogic? GetMinecartTrackLogic(Vec3I pos)
        {
            if (IsRail(_level, pos.X, pos.Y, pos.Z)) return new TrackLogic(_blocks, _level, pos);
            if (IsRail(_level, pos.X, pos.Y + 1, pos.Z)) return new TrackLogic(_blocks, _level, new Vec3I(pos.X, pos.Y + 1, pos.Z));
            if (IsRail(_level, pos.X, pos.Y - 1, pos.Z)) return new TrackLogic(_blocks, _level, new Vec3I(pos.X, pos.Y - 1, pos.Z));
            return null;
        }

        private bool IsRail(IWorldContext level, int x, int y, int z)
        {
            return _blocks.TryGetByProtocolId(level.Reader.GetBlockId(x, y, z), out var block) && RailBehavior.IsRail(block);
        }

        private bool IsConnectedTo(TrackLogic targetLogic)
        {
            foreach (var pos in _connectedTracks)
                if (pos.X == targetLogic._trackPos.X && pos.Z == targetLogic._trackPos.Z)
                    return true;

            return false;
        }

        private bool IsInTrack(Vec3I pos)
        {
            foreach (var connectedPos in _connectedTracks)
                if (connectedPos.X == pos.X && connectedPos.Z == pos.Z)
                    return true;

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
            _connectedTracks.Add(new Vec3I(targetLogic._trackPos.X, targetLogic._trackPos.Y, targetLogic._trackPos.Z));

            var north = IsInTrack(new Vec3I(_trackPos.X, _trackPos.Y, _trackPos.Z - 1));
            var south = IsInTrack(new Vec3I(_trackPos.X, _trackPos.Y, _trackPos.Z + 1));
            var west = IsInTrack(new Vec3I(_trackPos.X - 1, _trackPos.Y, _trackPos.Z));
            var east = IsInTrack(new Vec3I(_trackPos.X + 1, _trackPos.Y, _trackPos.Z));

            var meta = -1;
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

            var finalMeta = meta;
            if (_isPoweredRail) finalMeta = (_level.Reader.GetBlockMeta(_trackPos.X, _trackPos.Y, _trackPos.Z) & 8) | meta;

            _level.Writer.SetBlockMeta(_trackPos.X, _trackPos.Y, _trackPos.Z, finalMeta);
        }

        private bool AttemptConnectionAt(Vec3I pos)
        {
            var logic = GetMinecartTrackLogic(pos);
            if (logic == null) return false;

            logic.RefreshConnectedTracks();
            return logic.CanConnectTo(this);
        }
    }
}