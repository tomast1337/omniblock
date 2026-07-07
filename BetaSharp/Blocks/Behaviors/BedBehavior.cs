using BetaSharp.Entities;
using BetaSharp.Items;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Bed: a two-block structure (head + foot half, linked by meta direction) with sleep
///     interaction, explosion-on-no-spawn, and half-dependent drops (only the foot half drops the
///     item). The meta helpers and <see cref="FindWakeUpPosition" /> are public statics consumed
///     externally by <c>EntityPlayer</c>, <c>NaturalSpawner</c>, and the client's bed renderer — kept
///     here since there's no subclass left to hold them.
/// </summary>
public sealed class BedBehavior : IBlockInteractable, IBlockPhysics, IBlockLifecycle, IBlockVisuals
{
    private static readonly int s_bedId = Item.ByName("bed").Id;

    public static readonly Side[][] BedFacings =
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

        (int x, int y, int z) = (@event.X, @event.Y, @event.Z);

        int meta = @event.World.Reader.GetBlockMeta(x, y, z);
        if (!IsHeadOfBed(meta))
        {
            int direction = GetDirection(meta);
            x += s_bedOffsets[direction][0];
            z += s_bedOffsets[direction][1];

            if (@event.World.Reader.GetBlockId(x, y, z) != block.Id) return true;

            meta = @event.World.Reader.GetBlockMeta(x, y, z);
        }

        if (!@event.World.Dimension.HasWorldSpawn)
        {
            double posX = x + 0.5D;
            double posY = y + 0.5D;
            double posZ = z + 0.5D;
            @event.World.Writer.SetBlock(x, y, z, 0);

            int direction = GetDirection(meta);
            x += s_bedOffsets[direction][0];
            z += s_bedOffsets[direction][1];

            if (@event.World.Reader.GetBlockId(x, y, z) == block.Id)
            {
                @event.World.Writer.SetBlock(x, y, z, 0);
                posX = (posX + x + 0.5D) / 2.0D;
                posY = (posY + y + 0.5D) / 2.0D;
                posZ = (posZ + z + 0.5D) / 2.0D;
            }

            @event.World.CreateExplosion(null, x + 0.5F, y + 0.5F, z + 0.5F, 5.0F, true);
            return true;
        }

        if (IsBedOccupied(meta))
        {
            EntityPlayer? occupant = null;
            foreach (EntityPlayer otherPlayer in @event.World.Entities.Players)
            {
                if (!otherPlayer.IsSleeping)
                {
                    continue;
                }

                Vec3i? sleepingPos = otherPlayer.SleepingPos;
                if (sleepingPos != null && sleepingPos.Value.X == x && sleepingPos.Value.Y == y && sleepingPos.Value.Z == z)
                {
                    occupant = otherPlayer;
                }
            }

            if (occupant != null)
            {
                @event.Player.SendMessage("tile.bed.occupied");
                return true;
            }

            UpdateState(@event.World.Writer, x, y, z, meta, false);
        }

        SleepAttemptResult result = @event.Player.TrySleep(x, y, z);
        switch (result)
        {
            case SleepAttemptResult.OK:
                UpdateState(@event.World.Writer, x, y, z, meta, true);
                return true;
            case SleepAttemptResult.NOT_POSSIBLE_NOW:
                @event.Player.SendMessage("tile.bed.noSleep");
                break;
            case SleepAttemptResult.NOT_POSSIBLE_HERE:
                break;
            case SleepAttemptResult.TOO_FAR_AWAY:
                break;
            case SleepAttemptResult.OTHER_PROBLEM:
                break;
            default:
                throw new ArgumentException($"Invalid sleep attempt result: {result}");
        }

        return true;
    }
    public int GetDroppedItemId(Block block, int blockMeta, int defaultItemId) => IsHeadOfBed(blockMeta) ? 0 : s_bedId;

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        int blockMeta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        int direction = GetDirection(blockMeta);

        if (IsHeadOfBed(blockMeta))
        {
            if (@event.World.Reader.GetBlockId(@event.X - s_bedOffsets[direction][0], @event.Y, @event.Z - s_bedOffsets[direction][1]) != block.Id)
            {
                @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
            }
        }
        else if (@event.World.Reader.GetBlockId(@event.X + s_bedOffsets[direction][0], @event.Y, @event.Z + s_bedOffsets[direction][1]) != block.Id)
        {
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
            if (!@event.World.IsRemote)
            {
                block.DropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, blockMeta));
            }
        }
    }

    // ── IBlockVisuals ─────────────────────────────────────────────

    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
    {
        int direction = GetDirection(meta);
        Side sideFacing = BedFacings[direction][side.ToInt()];
        if (side == Side.Down) return BlockTextures.OakPlanks;

        if (IsHeadOfBed(meta))
        {
            if (sideFacing == Side.North) return BlockTextures.BedEndHead;
            if (sideFacing != Side.East && sideFacing != Side.West) return BlockTextures.BedTopHead;
            return BlockTextures.BedSideHead;
        }

        if (sideFacing == Side.South) return BlockTextures.BedEndFoot;

        if (sideFacing != Side.East && sideFacing != Side.West) return BlockTextures.BedTopFoot;

        return BlockTextures.BedSideFoot;
    }

    public static int GetDirection(int meta) => meta & 3;

    public static bool IsHeadOfBed(int meta) => (meta & 8) != 0;

    public static bool IsBedOccupied(int meta) => (meta & 4) != 0;

    public static void UpdateState(IBlockWriter worldWriter, int x, int y, int z, int meta, bool occupied)
    {
        if (occupied)
        {
            meta |= 4;
        }
        else
        {
            meta &= ~4;
        }

        worldWriter.SetBlockMeta(x, y, z, meta);
    }

    public static Vec3i? FindWakeUpPosition(IBlockReader reader, int x, int y, int z, int skip)
    {
        int blockMeta = reader.GetBlockMeta(x, y, z);
        int direction = GetDirection(blockMeta);

        if (IsHeadOfBed(blockMeta))
        {
            x -= s_bedOffsets[direction][0];
            z -= s_bedOffsets[direction][1];
        }

        for (int bedHalf = 0; bedHalf <= 1; ++bedHalf)
        {
            int centerX = x + s_bedOffsets[direction][0] * bedHalf;
            int centerZ = z + s_bedOffsets[direction][1] * bedHalf;

            int searchMinX = centerX - 1;
            int searchMinZ = centerZ - 1;
            int searchMaxX = centerX + 1;
            int searchMaxZ = centerZ + 1;

            for (int checkX = searchMinX; checkX <= searchMaxX; ++checkX)
            {
                for (int checkZ = searchMinZ; checkZ <= searchMaxZ; ++checkZ)
                {
                    if (!reader.ShouldSuffocate(checkX, y - 1, checkZ) ||
                        !reader.IsAir(checkX, y, checkZ) ||
                        !reader.IsAir(checkX, y + 1, checkZ))
                    {
                        continue;
                    }

                    if (skip <= 0)
                    {
                        return new Vec3i(checkX, y, checkZ);
                    }

                    --skip;
                }
            }
        }

        return null;
    }
}
