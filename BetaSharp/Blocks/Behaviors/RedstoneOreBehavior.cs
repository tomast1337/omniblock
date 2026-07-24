using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Redstone ore: touching it (walk, punch, click) lights it up and sparks; the lit block
///     reverts on its next tick. One instance is shared by both ore blocks — lit state is derived
///     from the block id. Assign to the Ticker and Interactable slots.
/// </summary>
public sealed class RedstoneOreBehavior : IBlockInteractable, IBlockTicker
{
    private static readonly Block s_redstoneOre = BlockRegistry.Get("redstone_ore");
    private static readonly Block s_litRedstoneOre = BlockRegistry.Get("lit_redstone_ore");

    public void OnBlockBreakStart(Block block, OnBlockBreakStartEvent @event) => Light(@event.World.Writer, @event.World.Reader, @event.World.Broadcaster, @event.X, @event.Y, @event.Z);

    public void OnSteppedOn(Block block, OnEntityStepEvent @event) => Light(@event.World.Writer, @event.World.Reader, @event.World.Broadcaster, @event.X, @event.Y, @event.Z);

    public bool OnUse(Block block, OnUseEvent @event)
    {
        Light(@event.World.Writer, @event.World.Reader, @event.World.Broadcaster, @event.X, @event.Y, @event.Z);
        return false;
    }

    public void OnTick(Block block, OnTickEvent @event)
    {
        if (IsLit(block))
        {
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, s_redstoneOre.id);
        }
    }

    public void RandomDisplayTick(Block block, OnTickEvent ctx)
    {
        if (IsLit(block))
        {
            SpawnParticles(ctx.World.Reader, ctx.World.Broadcaster, ctx.X, ctx.Y, ctx.Z);
        }
    }

    private static bool IsLit(Block block) => block.id == s_litRedstoneOre.id;

    private static void Light(IBlockWriter worldWriter, IBlockReader worldRead, WorldEventBroadcaster broadcaster, int x, int y, int z)
    {
        SpawnParticles(worldRead, broadcaster, x, y, z);
        if (worldRead.GetBlockId(x, y, z) == s_redstoneOre.id)
        {
            worldWriter.SetBlock(x, y, z, s_litRedstoneOre.id);
        }
    }

    private static void SpawnParticles(IBlockReader reader, WorldEventBroadcaster broadcaster, int x, int y, int z)
    {
        const double faceOffset = 1.0D / 16.0D;
        for (int direction = 0; direction < 6; ++direction)
        {
            double particleX = x + Random.Shared.NextSingle();
            double particleY = y + Random.Shared.NextSingle();
            double particleZ = z + Random.Shared.NextSingle();
            if (direction == 0 && !reader.IsOpaque(x, y + 1, z))
            {
                particleY = y + 1 + faceOffset;
            }

            if (direction == 1 && !reader.IsOpaque(x, y - 1, z))
            {
                particleY = y + 0 - faceOffset;
            }

            if (direction == 2 && !reader.IsOpaque(x, y, z + 1))
            {
                particleZ = z + 1 + faceOffset;
            }

            if (direction == 3 && !reader.IsOpaque(x, y, z - 1))
            {
                particleZ = z + 0 - faceOffset;
            }

            if (direction == 4 && !reader.IsOpaque(x + 1, y, z))
            {
                particleX = x + 1 + faceOffset;
            }

            if (direction == 5 && !reader.IsOpaque(x - 1, y, z))
            {
                particleX = x + 0 - faceOffset;
            }

            if (particleX < x || particleX > x + 1 || particleY < 0.0D || particleY > y + 1 || particleZ < z || particleZ > z + 1)
            {
                broadcaster.AddParticle("reddust", particleX, particleY, particleZ, 0.0D, 0.0D, 0.0D);
            }
        }
    }
}
