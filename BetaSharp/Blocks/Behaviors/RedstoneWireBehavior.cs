using BetaSharp.Items;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Redstone wire: power emission with directional connectivity, current propagation, survival
/// (breaks without solid ground), and the powered-dust particles. One shared instance assigned
/// to the Redstone, Physics, Ticker, Lifecycle, and Visuals slots.
/// <para>
/// <see cref="WiresProvidePower"/> is thread-local: propagation temporarily blinds the engine
/// to wires while measuring indirect power, exactly like the original static flag.
/// </para>
/// </summary>
public sealed class RedstoneWireBehavior : IRedstoneComponent, IBlockPhysics, IBlockTicker, IBlockLifecycle, IBlockVisuals
{
    private static readonly ThreadLocal<bool> s_wiresProvidePower = new(() => true);
    private static readonly int s_redstoneId = Item.ByName("redstone").Id;

    private readonly HashSet<BlockPos> _blocksNeedingUpdate = [];

    // ---- IRedstoneComponent ----

    public bool CanEmitRedstonePower(Block block) => s_wiresProvidePower.Value;

    public bool IsPoweringSide(Block block, IBlockReader reader, int x, int y, int z, int side)
    {
        if (!s_wiresProvidePower.Value) return false;
        if (reader.GetBlockMeta(x, y, z) == 0) return false;
        if (side == 1) return true;

        bool connectsMinusX = isPowerProviderOrWire(reader, x - 1, y, z, 1) || (!reader.ShouldSuffocate(x - 1, y, z) && isPowerProviderOrWire(reader, x - 1, y - 1, z, -1));
        bool connectsPlusX = isPowerProviderOrWire(reader, x + 1, y, z, 3) || (!reader.ShouldSuffocate(x + 1, y, z) && isPowerProviderOrWire(reader, x + 1, y - 1, z, -1));
        bool connectsMinusZ = isPowerProviderOrWire(reader, x, y, z - 1, 2) || (!reader.ShouldSuffocate(x, y, z - 1) && isPowerProviderOrWire(reader, x, y - 1, z - 1, -1));
        bool connectsPlusZ = isPowerProviderOrWire(reader, x, y, z + 1, 0) || (!reader.ShouldSuffocate(x, y, z + 1) && isPowerProviderOrWire(reader, x, y - 1, z + 1, -1));

        if (!reader.ShouldSuffocate(x, y + 1, z))
        {
            if (reader.ShouldSuffocate(x - 1, y, z) && isPowerProviderOrWire(reader, x - 1, y + 1, z, -1)) connectsMinusX = true;
            if (reader.ShouldSuffocate(x + 1, y, z) && isPowerProviderOrWire(reader, x + 1, y + 1, z, -1)) connectsPlusX = true;
            if (reader.ShouldSuffocate(x, y, z - 1) && isPowerProviderOrWire(reader, x, y + 1, z - 1, -1)) connectsMinusZ = true;
            if (reader.ShouldSuffocate(x, y, z + 1) && isPowerProviderOrWire(reader, x, y + 1, z + 1, -1)) connectsPlusZ = true;
        }

        return !connectsMinusZ && !connectsPlusX && !connectsMinusX && !connectsPlusZ && side is >= 2 and <= 5 ||
               side == 2 && connectsMinusZ && !connectsMinusX && !connectsPlusX ||
               side == 3 && connectsPlusZ && !connectsMinusX && !connectsPlusX ||
               side == 4 && connectsMinusX && !connectsMinusZ && !connectsPlusZ ||
               side == 5 && connectsPlusX && !connectsMinusZ && !connectsPlusZ;
    }

    public bool IsStrongPoweringSide(Block block, IBlockReader reader, int x, int y, int z, int side) => s_wiresProvidePower.Value && IsPoweringSide(block, reader, x, y, z, side);

    // ---- IBlockVisuals ----

    public int GetColorMultiplier(Block block, IBlockReader reader, int x, int y, int z, int defaultColor) => 8388608;

    // ---- IBlockPhysics ----

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event) => @event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z);

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (@event.World.IsRemote) return;

        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        // Direct ground check — not block.canPlaceAt, which also tests replaceability of the
        // wire's own (occupied) position and would always fail here.
        if (!@event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z))
        {
            block.dropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, meta));
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        }
        else
        {
            updateAndPropagateCurrentStrength(@event.World, @event.X, @event.Y, @event.Z);
        }
    }

    // ---- IBlockLifecycle ----

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (@event.World.IsRemote) return;

        updateAndPropagateCurrentStrength(@event.World, @event.X, @event.Y, @event.Z);
        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y + 1, @event.Z, block.id);
        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y - 1, @event.Z, block.id);
        NotifySurroundingWires(@event.World, @event.X, @event.Y, @event.Z);
    }

    public void OnBreak(Block block, OnBreakEvent @event)
    {
        if (@event.World.IsRemote) return;

        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y + 1, @event.Z, block.id);
        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y - 1, @event.Z, block.id);
        updateAndPropagateCurrentStrength(@event.World, @event.X, @event.Y, @event.Z);
        NotifySurroundingWires(@event.World, @event.X, @event.Y, @event.Z);
    }

    /// <summary>
    /// Notifies the wires laterally adjacent to (x, y, z), stepping up over solid neighbors
    /// and down past non-solid ones — identical on place and on break.
    /// </summary>
    private void NotifySurroundingWires(IWorldContext level, int x, int y, int z)
    {
        NotifyWireNeighborsOfNeighborChange(level, x - 1, y, z);
        NotifyWireNeighborsOfNeighborChange(level, x + 1, y, z);
        NotifyWireNeighborsOfNeighborChange(level, x, y, z - 1);
        NotifyWireNeighborsOfNeighborChange(level, x, y, z + 1);

        NotifyWireNeighborsOfNeighborChange(level, x - 1, y + (level.Reader.ShouldSuffocate(x - 1, y, z) ? 1 : -1), z);
        NotifyWireNeighborsOfNeighborChange(level, x + 1, y + (level.Reader.ShouldSuffocate(x + 1, y, z) ? 1 : -1), z);
        NotifyWireNeighborsOfNeighborChange(level, x, y + (level.Reader.ShouldSuffocate(x, y, z - 1) ? 1 : -1), z - 1);
        NotifyWireNeighborsOfNeighborChange(level, x, y + (level.Reader.ShouldSuffocate(x, y, z + 1) ? 1 : -1), z + 1);
    }

    // ---- IBlockTicker ----

    public void RandomDisplayTick(Block block, OnTickEvent @event)
    {
        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        if (meta <= 0) return;

        double x = @event.X + 0.5D + (@event.World.Random.NextFloat() - 0.5D) * 0.2D;
        double y = @event.Y + 1.0F / 16.0F;
        double z = @event.Z + 0.5D + (@event.World.Random.NextFloat() - 0.5D) * 0.2D;
        float powerRatio = meta / 15.0F;
        float xVel = powerRatio * 0.6F + 0.4F;

        float yVel = powerRatio * powerRatio * 0.7F - 0.5F;
        float zVel = powerRatio * powerRatio * 0.6F - 0.7F;
        if (yVel < 0.0F)
        {
            yVel = 0.0F;
        }

        if (zVel < 0.0F)
        {
            zVel = 0.0F;
        }

        @event.World.Broadcaster.AddParticle("reddust", x, y, z, xVel, yVel, zVel);
    }

    // ---- Propagation ----

    private void updateAndPropagateCurrentStrength(IWorldContext level, int startX, int startY, int startZ)
    {
        calculateCurrentChanges(level, startX, startY, startZ, -1, -1, -1);

        List<BlockPos> updateList = [.. _blocksNeedingUpdate];
        _blocksNeedingUpdate.Clear();

        foreach (BlockPos pos in updateList)
        {
            level.Broadcaster.NotifyNeighbors(pos.x, pos.y, pos.z, Block.RedstoneWire.id);
        }
    }

    private void calculateCurrentChanges(IWorldContext level, int x, int y, int z, int sourceX, int sourceY, int sourceZ)
    {
        int oldMeta = level.Reader.GetBlockMeta(x, y, z);

        s_wiresProvidePower.Value = false;
        bool isIndirectlyPowered = level.Redstone.IsPowered(x, y, z);
        s_wiresProvidePower.Value = true;

        int maxCurrent = 0;
        if (isIndirectlyPowered)
        {
            maxCurrent = 15;
        }
        else
        {
            for (int dir = 0; dir < 4; dir++)
            {
                int nx = x + (dir == 0 ? -1 : dir == 1 ? 1 : 0);
                int nz = z + (dir == 2 ? -1 : dir == 3 ? 1 : 0);

                if (nx != sourceX || nz != sourceZ)
                {
                    maxCurrent = getMaxCurrentStrength(level.Reader, nx, y, nz, maxCurrent);
                }

                if (level.Reader.ShouldSuffocate(nx, y, nz))
                {
                    if (!level.Reader.ShouldSuffocate(x, y + 1, z) && (nx != sourceX || nz != sourceZ))
                    {
                        maxCurrent = getMaxCurrentStrength(level.Reader, nx, y + 1, nz, maxCurrent);
                    }
                }
                else if (nx != sourceX || nz != sourceZ)
                {
                    maxCurrent = getMaxCurrentStrength(level.Reader, nx, y - 1, nz, maxCurrent);
                }
            }

            if (maxCurrent > 0) maxCurrent--;
        }

        if (oldMeta == maxCurrent) return;

        level.Writer.SetBlockMeta(x, y, z, maxCurrent);

        level.Broadcaster.SetBlocksDirty(x - 1, y - 1, z - 1, x + 1, y + 1, z + 1);

        for (int dir = 0; dir < 4; dir++)
        {
            int nx = x + (dir == 0 ? -1 : dir == 1 ? 1 : 0);
            int nz = z + (dir == 2 ? -1 : dir == 3 ? 1 : 0);
            int ny = y - 1;

            if (level.Reader.ShouldSuffocate(nx, y, nz)) ny += 2;

            int neighborMax = getMaxCurrentStrength(level.Reader, nx, y, nz, -1);
            if (neighborMax >= 0 && neighborMax != (maxCurrent > 0 ? maxCurrent - 1 : 0))
            {
                calculateCurrentChanges(level, nx, y, nz, x, y, z);
            }

            neighborMax = getMaxCurrentStrength(level.Reader, nx, ny, nz, -1);
            if (neighborMax >= 0 && neighborMax != (maxCurrent > 0 ? maxCurrent - 1 : 0))
            {
                calculateCurrentChanges(level, nx, ny, nz, x, y, z);
            }
        }

        if (oldMeta == 0 || maxCurrent == 0)
        {
            _blocksNeedingUpdate.Add(new BlockPos(x, y, z));
            _blocksNeedingUpdate.Add(new BlockPos(x - 1, y, z));
            _blocksNeedingUpdate.Add(new BlockPos(x + 1, y, z));
            _blocksNeedingUpdate.Add(new BlockPos(x, y - 1, z));
            _blocksNeedingUpdate.Add(new BlockPos(x, y + 1, z));
            _blocksNeedingUpdate.Add(new BlockPos(x, y, z - 1));
            _blocksNeedingUpdate.Add(new BlockPos(x, y, z + 1));
        }
    }

    private void NotifyWireNeighborsOfNeighborChange(IWorldContext level, int x, int y, int z)
    {
        if (level.Reader.GetBlockId(x, y, z) != Block.RedstoneWire.id) return;

        level.Broadcaster.NotifyNeighbors(x, y, z, Block.RedstoneWire.id);
        level.Broadcaster.NotifyNeighbors(x - 1, y, z, Block.RedstoneWire.id);
        level.Broadcaster.NotifyNeighbors(x + 1, y, z, Block.RedstoneWire.id);
        level.Broadcaster.NotifyNeighbors(x, y, z - 1, Block.RedstoneWire.id);
        level.Broadcaster.NotifyNeighbors(x, y, z + 1, Block.RedstoneWire.id);
        level.Broadcaster.NotifyNeighbors(x, y - 1, z, Block.RedstoneWire.id);
        level.Broadcaster.NotifyNeighbors(x, y + 1, z, Block.RedstoneWire.id);
    }

    private static int getMaxCurrentStrength(IBlockReader reader, int x, int y, int z, int power)
    {
        if (reader.GetBlockId(x, y, z) != Block.RedstoneWire.id) return power;
        int currentStrength = reader.GetBlockMeta(x, y, z);
        return currentStrength > power ? currentStrength : power;
    }

    /// <summary>Connectivity test shared with the client wire renderer.</summary>
    public static bool isPowerProviderOrWire(IBlockReader reader, int x, int y, int z, int direction)
    {
        int blockId = reader.GetBlockId(x, y, z);
        if (blockId == 0) return false;
        if (blockId == Block.RedstoneWire.id) return true;
        if (blockId == Block.StonePressurePlate.id ||
            blockId == Block.WoodenPressurePlate.id ||
            blockId == Block.Button.id ||
            blockId == Block.Lever.id)
            return true;

        if (blockId != Block.Repeater.id && blockId != Block.PoweredRepeater.id) return Block.Blocks[blockId].canEmitRedstonePower();

        if (direction < 0) return false;
        int meta = reader.GetBlockMeta(x, y, z);
        int orientation = meta & 3;
        int opposite = (orientation + 2) & 3;
        return direction == orientation || direction == opposite;
    }
}
