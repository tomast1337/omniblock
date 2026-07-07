using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Redstone repeater: emits delayed, directional power out of its facing side. One instance is
/// shared by the unlit and powered blocks — lit state is derived from the block id, and the
/// delay state machine swaps between the two ids. Assign to all six slots.
/// </summary>
public sealed class RepeaterBehavior : IRedstoneComponent, IBlockTicker, IBlockPhysics, IBlockInteractable, IBlockLifecycle, IBlockVisuals
{
    public static readonly float[] RenderOffset = [-0.0625f, 1.0f / 16.0f, 0.1875f, 0.3125f];
    private static readonly int[] s_delay = [1, 2, 3, 4];

    private static bool IsLit(Block block) => block.id == Block.PoweredRepeater.id;

    public bool CanPlaceAt(Block block, CanPlaceAtContext context) => context.World.Reader.ShouldSuffocate(context.X, context.Y - 1, context.Z);

    public bool CanGrow(Block block, OnTickEvent @event) => @event.World.Reader.ShouldSuffocate(@event.X, @event.Y - 1, @event.Z);

    public void OnTick(Block block, OnTickEvent @event)
    {
        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        bool powered = isPowered(@event.World.Reader, @event.World.Redstone, @event.X, @event.Y, @event.Z, meta);

        switch (IsLit(block))
        {
            case true when !powered:
                @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, Block.Repeater.id, meta);
                break;
            case false:
                {
                    @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, Block.PoweredRepeater.id, meta);

                    if (!powered)
                    {
                        int delaySetting = (meta & 12) >> 2;
                        @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, Block.PoweredRepeater.id, s_delay[delaySetting] * 2);
                    }

                    break;
                }
        }
    }

    public int GetTexture(Block block, Side side, int defaultTexture) => textureFor(block, side);

    public int GetTexture(Block block, Side side, int meta, int defaultTexture) => textureFor(block, side);

    private static int textureFor(Block block, Side side) => side switch
    {
        Side.Down => IsLit(block) ? BlockTextures.RedstoneTorchLit : BlockTextures.RedstoneTorchUnlit,
        Side.Up => IsLit(block) ? BlockTextures.RepeaterTopLit : BlockTextures.RepeaterTopUnlit,
        _ => BlockTextures.StoneSlabSide
    };

    public bool IsSideVisible(Block block, IBlockReader reader, int x, int y, int z, Side side, bool defaultVisibility) => side != Side.Down && side != Side.Up;

    public bool IsStrongPoweringSide(Block block, IBlockReader world, int x, int y, int z, int side) => IsPoweringSide(block, world, x, y, z, side);

    public bool IsPoweringSide(Block block, IBlockReader reader, int x, int y, int z, int side)
    {
        if (!IsLit(block)) return false;

        int facing = reader.GetBlockMeta(x, y, z) & 3;
        return facing == 0 && side == 3 ||
               facing == 1 && side == 4 ||
               facing == 2 && side == 2 ||
               facing == 3 && side == 5;
    }

    public bool CanEmitRedstonePower(Block block) => true;

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (!block.canGrow(@event))
        {
            block.dropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.Meta));
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        }
        else
        {
            int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
            bool powered = isPowered(@event.World.Reader, @event.World.Redstone, @event.X, @event.Y, @event.Z, meta);
            int delaySetting = (meta & 12) >> 2;
            if (IsLit(block) && !powered || !IsLit(block) && powered)
            {
                @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, block.id, s_delay[delaySetting] * 2);
            }
        }
    }

    public void OnBreak(Block block, OnBreakEvent @event)
    {
        if (@event.World.IsRemote) return;

        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        NotifyTargetNeighbors(block, @event.World, @event.X, @event.Y, @event.Z, meta);
    }

    private static bool isPowered(IBlockReader world, RedstoneEngine redstoneEngine, int x, int y, int z, int meta)
    {
        int facing = meta & 3;
        return facing switch
        {
            0 => redstoneEngine.IsPoweringSide(x, y, z + 1, 3) || (world.GetBlockId(x, y, z + 1) == Block.RedstoneWire.id && world.GetBlockMeta(x, y, z + 1) > 0),
            1 => redstoneEngine.IsPoweringSide(x - 1, y, z, 4) || (world.GetBlockId(x - 1, y, z) == Block.RedstoneWire.id && world.GetBlockMeta(x - 1, y, z) > 0),
            2 => redstoneEngine.IsPoweringSide(x, y, z - 1, 2) || (world.GetBlockId(x, y, z - 1) == Block.RedstoneWire.id && world.GetBlockMeta(x, y, z - 1) > 0),
            3 => redstoneEngine.IsPoweringSide(x + 1, y, z, 5) || (world.GetBlockId(x + 1, y, z) == Block.RedstoneWire.id && world.GetBlockMeta(x + 1, y, z) > 0),
            _ => false
        };
    }

    public bool OnUse(Block block, OnUseEvent ctx)
    {
        int meta = ctx.World.Reader.GetBlockMeta(ctx.X, ctx.Y, ctx.Z);
        int newDelaySetting = (meta & 12) >> 2;
        newDelaySetting = ((newDelaySetting + 1) << 2) & 12;
        ctx.World.Writer.SetBlockMeta(ctx.X, ctx.Y, ctx.Z, newDelaySetting | (meta & 3));
        return true;
    }

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (@event.Placer != null)
        {
            float yaw = @event.Placer.Yaw;
            int facing = ((MathHelper.Floor(yaw * 4.0F / 360.0F + 0.5D) & 3) + 2) % 4;
            @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, facing);
        }

        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);

        bool powered = isPowered(@event.World.Reader, @event.World.Redstone, @event.X, @event.Y, @event.Z, meta);
        if (powered)
        {
            @event.World.TickScheduler.ScheduleBlockUpdate(@event.X, @event.Y, @event.Z, block.id, 1);
        }

        @event.World.Broadcaster.NotifyNeighbors(@event.X + 1, @event.Y, @event.Z, block.id);
        @event.World.Broadcaster.NotifyNeighbors(@event.X - 1, @event.Y, @event.Z, block.id);
        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y, @event.Z + 1, block.id);
        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y, @event.Z - 1, block.id);
        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y - 1, @event.Z, block.id);
        @event.World.Broadcaster.NotifyNeighbors(@event.X, @event.Y + 1, @event.Z, block.id);

        if (!@event.World.IsRemote) NotifyTargetNeighbors(block, @event.World, @event.X, @event.Y, @event.Z, meta);
    }

    private static void NotifyTargetNeighbors(Block block, IWorldContext ctx, int x, int y, int z, int meta)
    {
        int facing = meta & 3;
        int targetX = x;
        int targetZ = z;

        switch (facing)
        {
            case 0: targetZ--; break;
            case 1: targetX++; break;
            case 2: targetZ++; break;
            case 3: targetX--; break;
        }

        ctx.Broadcaster.NotifyNeighbors(targetX, y, targetZ, block.id);
    }

    public void RandomDisplayTick(Block block, OnTickEvent ctx)
    {
        if (!IsLit(block)) return;

        int meta = ctx.World.Reader.GetBlockMeta(ctx.X, ctx.Y, ctx.Z);
        double particleX = ctx.X + 0.5F + (Random.Shared.NextSingle() - 0.5F) * 0.2D;
        double particleY = ctx.Y + 0.4F + (Random.Shared.NextSingle() - 0.5F) * 0.2D;
        double particleZ = ctx.Z + 0.5F + (Random.Shared.NextSingle() - 0.5F) * 0.2D;
        double offsetX = 0.0D;
        double offsetY = 0.0D;
        if (Random.Shared.Next(2) == 0)
        {
            switch (meta & 3)
            {
                case 0:
                    offsetY = -0.3125D;
                    break;
                case 1:
                    offsetX = 0.3125D;
                    break;
                case 2:
                    offsetY = 0.3125D;
                    break;
                case 3:
                    offsetX = -0.3125D;
                    break;
            }
        }
        else
        {
            int delayIndex = (meta & 12) >> 2;
            switch (meta & 3)
            {
                case 0:
                    offsetY = RenderOffset[delayIndex];
                    break;
                case 1:
                    offsetX = -RenderOffset[delayIndex];
                    break;
                case 2:
                    offsetY = -RenderOffset[delayIndex];
                    break;
                case 3:
                    offsetX = RenderOffset[delayIndex];
                    break;
            }
        }

        ctx.World.Broadcaster.AddParticle("reddust", particleX + offsetX, particleY, particleZ + offsetY, 0.0D, 0.0D, 0.0D);
    }
}
