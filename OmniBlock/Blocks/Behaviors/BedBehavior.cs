using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Bed: a two-block structure (head + foot half, linked by meta direction) with sleep
///     interaction, explosion-on-no-spawn, and half-dependent drops (only the foot half drops the
///     item). The meta-helpers and <see cref="FindWakeUpPosition" /> are public statics consumed
///     externally by <c>EntityPlayer</c>, <c>NaturalSpawner</c>, and the client's bed renderer.
/// </summary>
public sealed class BedBehavior(int bottom, int footTop, int footSide, int footEnd, int headTop, int headSide, int headEnd, Item bedItem) : IBlockInteractable, IBlockPhysics, IBlockLifecycle, IBlockVisuals
{
    private static readonly Side[][] s_bedFacings =
    [
        [Side.Up, Side.Down, Side.South, Side.North, Side.East, Side.West],
        [Side.Up, Side.Down, Side.East, Side.West, Side.North, Side.South],
        [Side.Up, Side.Down, Side.North, Side.South, Side.West, Side.East],
        [Side.Up, Side.Down, Side.West, Side.East, Side.South, Side.North]
    ];

    private static readonly int[][] s_bedOffsets = [[0, 1], [-1, 0], [0, -1], [1, 0]];

    public bool OnUse(Block block, OnUseEvent @event)
    {
        if (@event.World.IsRemote) return true;

        var (x, y, z) = (@event.X, @event.Y, @event.Z);

        var meta = @event.World.Reader.GetBlockMeta(x, y, z);
        if (!IsHeadOfBed(meta))
        {
            var direction = GetDirection(meta);
            x += s_bedOffsets[direction][0];
            z += s_bedOffsets[direction][1];

            if (@event.World.Reader.GetBlockId(x, y, z) != block.Id) return true;

            meta = @event.World.Reader.GetBlockMeta(x, y, z);
        }

        if (!@event.World.Dimension.HasWorldSpawn)
        {
            @event.World.Writer.SetBlock(x, y, z, 0);

            var direction = GetDirection(meta);
            x += s_bedOffsets[direction][0];
            z += s_bedOffsets[direction][1];

            if (@event.World.Reader.GetBlockId(x, y, z) == block.Id) @event.World.Writer.SetBlock(x, y, z, 0);

            @event.World.CreateExplosion(null, x + 0.5F, y + 0.5F, z + 0.5F, 5.0F, true);
            return true;
        }

        if (IsBedOccupied(meta))
        {
            EntityPlayer? occupant = null;
            foreach (var otherPlayer in @event.World.Entities.Players)
            {
                if (!otherPlayer.IsSleeping) continue;

                var sleepingPos = otherPlayer.SleepingPos;
                if (sleepingPos != null && sleepingPos.Value.X == x && sleepingPos.Value.Y == y && sleepingPos.Value.Z == z) occupant = otherPlayer;
            }

            if (occupant != null)
            {
                @event.Player.SendMessage("tile.bed.occupied");
                return true;
            }

            UpdateState(@event.World.Writer, x, y, z, meta, false);
        }

        var result = @event.Player.TrySleep(x, y, z);
        switch (result)
        {
            case SleepAttemptResult.OK:
                UpdateState(@event.World.Writer, x, y, z, meta, true);
                break;
            case SleepAttemptResult.NOT_POSSIBLE_NOW:
                @event.Player.SendMessage("tile.bed.noSleep");
                break;
            case SleepAttemptResult.NOT_POSSIBLE_HERE:
            case SleepAttemptResult.TOO_FAR_AWAY:
            case SleepAttemptResult.OTHER_PROBLEM:
                break;
            default:
                throw new ArgumentException($"Invalid sleep attempt result: {result}");
        }

        return true;
    }

    public int GetDroppedItemId(Block block, int blockMeta, int defaultItemId) => IsHeadOfBed(blockMeta) ? 0 : bedItem.Id;

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        var blockMeta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        var direction = GetDirection(blockMeta);

        if (IsHeadOfBed(blockMeta))
        {
            if (@event.World.Reader.GetBlockId(@event.X - s_bedOffsets[direction][0], @event.Y, @event.Z - s_bedOffsets[direction][1]) != block.Id) @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        }
        else if (@event.World.Reader.GetBlockId(@event.X + s_bedOffsets[direction][0], @event.Y, @event.Z + s_bedOffsets[direction][1]) != block.Id)
        {
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
            if (!@event.World.IsRemote) block.DropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, blockMeta));
        }
    }

    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
    {
        var direction = GetDirection(meta);
        var sideFacing = s_bedFacings[direction][side.ToInt()];
        if (side == Side.Down) return bottom;

        if (IsHeadOfBed(meta))
        {
            if (sideFacing == Side.North) return headEnd;
            if (sideFacing != Side.East && sideFacing != Side.West) return headTop;
            return headSide;
        }

        if (sideFacing == Side.South) return footEnd;

        if (sideFacing != Side.East && sideFacing != Side.West) return footTop;

        return footSide;
    }

    public static int GetDirection(int meta) => meta & 3;

    public static bool IsHeadOfBed(int meta) => (meta & 8) != 0;

    private static bool IsBedOccupied(int meta) => (meta & 4) != 0;

    public static void UpdateState(IBlockWriter worldWriter, int x, int y, int z, int meta, bool occupied)
    {
        if (occupied)
            meta |= 4;
        else
            meta &= ~4;

        worldWriter.SetBlockMeta(x, y, z, meta);
    }

    public static Vec3I? FindWakeUpPosition(IBlockReader reader, int x, int y, int z, int skip)
    {
        var blockMeta = reader.GetBlockMeta(x, y, z);
        var direction = GetDirection(blockMeta);

        if (IsHeadOfBed(blockMeta))
        {
            x -= s_bedOffsets[direction][0];
            z -= s_bedOffsets[direction][1];
        }

        for (var bedHalf = 0; bedHalf <= 1; ++bedHalf)
        {
            var centerX = x + s_bedOffsets[direction][0] * bedHalf;
            var centerZ = z + s_bedOffsets[direction][1] * bedHalf;

            var searchMinX = centerX - 1;
            var searchMinZ = centerZ - 1;
            var searchMaxX = centerX + 1;
            var searchMaxZ = centerZ + 1;

            for (var checkX = searchMinX; checkX <= searchMaxX; ++checkX)
            for (var checkZ = searchMinZ; checkZ <= searchMaxZ; ++checkZ)
            {
                if (!reader.ShouldSuffocate(checkX, y - 1, checkZ) ||
                    !reader.IsAir(checkX, y, checkZ) ||
                    !reader.IsAir(checkX, y + 1, checkZ))
                    continue;

                if (skip <= 0) return new Vec3I(checkX, y, checkZ);

                --skip;
            }
        }

        return null;
    }
}
