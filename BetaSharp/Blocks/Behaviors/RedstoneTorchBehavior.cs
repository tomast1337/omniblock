using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Redstone torch power emission, burnout tracking, and lit/unlit toggling. One instance is
/// shared by both torch blocks — lit state is derived from the block id, and the burnout
/// history must span both blocks since a toggling torch alternates between them.
/// <para>
/// Wall-mount placement/facing/support-break is delegated to the shared <see cref="WallMountBehavior"/>
/// torch instance (composition, not inheritance, now that <c>BlockTorch</c> is flattened) —
/// this class layers redstone-specific neighbor notification and burnout scheduling on top.
/// Assign to the Redstone, Ticker, Visuals, Physics, and Lifecycle slots.
/// </para>
/// </summary>
public sealed class RedstoneTorchBehavior : IRedstoneComponent, IBlockTicker, IBlockVisuals, IBlockPhysics, IBlockLifecycle
{
    private const double VerticalOffset = 0.22F;
    private const double HorizontalOffset = 0.27F;

    private readonly List<RedstoneUpdateInfo> _torchUpdates = [];
    private readonly Lock _updateLock = new();
    private readonly WallMountBehavior _torchPhysics;

    public RedstoneTorchBehavior(WallMountBehavior torchPhysics) => _torchPhysics = torchPhysics;

    private static bool IsLit(Block block) => block.id == Block.LitRedstoneTorch.id;

    public bool CanEmitRedstonePower(Block block) => true;

    public bool IsPoweringSide(Block block, IBlockReader reader, int x, int y, int z, int side)
    {
        if (!IsLit(block)) return false;

        int meta = reader.GetBlockMeta(x, y, z);
        return (meta != 5 || side != 1) && (meta != 3 || side != 3) && (meta != 4 || side != 2) && (meta != 1 || side != 5) && (meta != 2 || side != 4);
    }

    public bool IsStrongPoweringSide(Block block, IBlockReader reader, int x, int y, int z, int side) => side == 0 && IsPoweringSide(block, reader, x, y, z, side);

    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
        => side == Side.Up ? Block.RedstoneWire.GetTexture(side, meta) : defaultTexture;

    private bool isBurnedOut(OnTickEvent ctx, bool recordUpdate, long currentTime)
    {
        lock (_updateLock)
        {
            if (recordUpdate)
            {
                _torchUpdates.Add(new RedstoneUpdateInfo(ctx.X, ctx.Y, ctx.Z, currentTime));
            }

            int updateCount = 0;

            foreach (var updateInfo in _torchUpdates)
            {
                if (updateInfo.x != ctx.X || updateInfo.y != ctx.Y || updateInfo.z != ctx.Z) continue;

                ++updateCount;
                if (updateCount >= 8) return true;
            }

            return false;
        }
    }

    private static bool shouldUnpower(OnTickEvent @event)
    {
        (int x, int y, int z) = (@event.X, @event.Y, @event.Z);
        RedstoneEngine redstoneEngine = @event.World.Redstone;
        int meta = @event.World.Reader.GetBlockMeta(x, y, z);
        return (meta == 5 && redstoneEngine.IsPoweringSide(x, y - 1, z, 0)) || (meta == 3 && redstoneEngine.IsPoweringSide(x, y, z - 1, 2)) ||
               (meta == 4 && redstoneEngine.IsPoweringSide(x, y, z + 1, 3)) || (meta == 1 && redstoneEngine.IsPoweringSide(x - 1, y, z, 4)) || (meta == 2 && redstoneEngine.IsPoweringSide(x + 1, y, z, 5));
    }

    public void OnTick(Block block, OnTickEvent @event)
    {
        (int x, int y, int z) = (@event.X, @event.Y, @event.Z);
        bool shouldTurnOff = shouldUnpower(@event);

        long currentTime = @event.World.GetTime();

        lock (_updateLock)
        {
            while (_torchUpdates.Count > 0 && currentTime - _torchUpdates[0].updateTime > 60L)
            {
                _torchUpdates.RemoveAt(0);
            }
        }

        if (IsLit(block))
        {
            if (!shouldTurnOff) return;

            @event.World.Writer.SetBlock(x, y, z, Block.RedstoneTorch.id, @event.World.Reader.GetBlockMeta(x, y, z));

            if (!isBurnedOut(@event, true, currentTime)) return;

            @event.World.Broadcaster.WorldEvent(1004, x, y, z, 0);

            for (int particleIndex = 0; particleIndex < 5; ++particleIndex)
            {
                double particleX = x + Random.Shared.NextDouble() * 0.6D + 0.2D;
                double particleY = y + Random.Shared.NextDouble() * 0.6D + 0.2D;
                double particleZ = z + Random.Shared.NextDouble() * 0.6D + 0.2D;
                @event.World.Broadcaster.AddParticle("smoke", particleX, particleY, particleZ, 0.0D, 0.0D, 0.0D);
            }

            int spatialBias = (x + y + z) % 3;
            @event.World.TickScheduler.ScheduleBlockUpdate(x, y, z, Block.RedstoneTorch.id, 160 + spatialBias);
        }
        else if (!shouldTurnOff && !isBurnedOut(@event, false, currentTime))
        {
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, Block.LitRedstoneTorch.id, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z));
        }
    }

    public void RandomDisplayTick(Block block, OnTickEvent @event)
    {
        if (!IsLit(block)) return;

        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        double particleX = @event.X + 0.5F + (Random.Shared.NextSingle() - 0.5F) * 0.2D;
        double particleY = @event.Y + 0.7F + (Random.Shared.NextSingle() - 0.5F) * 0.2D;
        double particleZ = @event.Z + 0.5F + (Random.Shared.NextSingle() - 0.5F) * 0.2D;
        switch (meta)
        {
            case 1:
                @event.World.Broadcaster.AddParticle("reddust", particleX - HorizontalOffset, particleY + VerticalOffset, particleZ, 0.0D, 0.0D, 0.0D);
                break;
            case 2:
                @event.World.Broadcaster.AddParticle("reddust", particleX + HorizontalOffset, particleY + VerticalOffset, particleZ, 0.0D, 0.0D, 0.0D);
                break;
            case 3:
                @event.World.Broadcaster.AddParticle("reddust", particleX, particleY + VerticalOffset, particleZ - HorizontalOffset, 0.0D, 0.0D, 0.0D);
                break;
            case 4:
                @event.World.Broadcaster.AddParticle("reddust", particleX, particleY + VerticalOffset, particleZ + HorizontalOffset, 0.0D, 0.0D, 0.0D);
                break;
            default:
                @event.World.Broadcaster.AddParticle("reddust", particleX, particleY, particleZ, 0.0D, 0.0D, 0.0D);
                break;
        }
    }

    // ── IBlockPhysics (wall-mount delegated to the torch behavior) ───

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event) => _torchPhysics.CanPlaceAt(block, @event);

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
        => _torchPhysics.UpdateBoundingBox(block, reader, x, y, z);

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        _torchPhysics.NeighborUpdate(block, @event);
        @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, block.id, block.getTickRate());
    }

    // ── IBlockLifecycle ───────────────────────────────────────────

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (@event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z) == 0)
        {
            _torchPhysics.OnPlaced(block, @event);
        }

        if (!IsLit(block)) return;

        NotifyAllNeighbors(@event.World, @event.X, @event.Y, @event.Z, block.id);
    }

    public void OnBreak(Block block, OnBreakEvent @event)
    {
        if (!IsLit(block)) return;

        NotifyAllNeighbors(@event.World, @event.X, @event.Y, @event.Z, block.id);
    }

    private static void NotifyAllNeighbors(IWorldContext world, int x, int y, int z, int id)
    {
        world.Broadcaster.NotifyNeighbors(x, y - 1, z, id);
        world.Broadcaster.NotifyNeighbors(x, y + 1, z, id);
        world.Broadcaster.NotifyNeighbors(x - 1, y, z, id);
        world.Broadcaster.NotifyNeighbors(x + 1, y, z, id);
        world.Broadcaster.NotifyNeighbors(x, y, z - 1, id);
        world.Broadcaster.NotifyNeighbors(x, y, z + 1, id);
    }
}
