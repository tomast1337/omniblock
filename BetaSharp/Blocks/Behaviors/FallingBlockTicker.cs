using BetaSharp.Blocks.Materials;
using BetaSharp.Entities;

namespace BetaSharp.Blocks.Behaviors;

public class FallingBlockTicker : IBlockTicker
{
    private static readonly ThreadLocal<bool> s_fallInstantly = new(() => false);

    private const sbyte CheckRadius = 32;

    public static bool FallInstantly
    {
        get => s_fallInstantly.Value;
        set => s_fallInstantly.Value = value;
    }

    public void OnTick(Block block, OnTickEvent @event) => processFall(block, @event);

    private static void processFall(Block block, OnTickEvent @event)
    {
        (int x, int y, int z) = (@event.X, @event.Y, @event.Z);
        if (y <= 0 || !canFallThrough(new OnTickEvent(@event.World, x, y - 1, z, 0, @event.BlockId))) return;

        if (!FallInstantly && @event.World.ChunkHost.IsRegionLoaded(x - CheckRadius, y - CheckRadius, z - CheckRadius, x + CheckRadius, y + CheckRadius, z + CheckRadius))
        {
            EntityFallingSand fallingSand = new(@event.World, x + 0.5F, y + 0.5F, z + 0.5F, block.id);
            @event.World.Entities.SpawnEntity(fallingSand);
        }
        else
        {
            @event.World.Writer.SetBlock(x, y, z, 0);

            while (canFallThrough(new OnTickEvent(@event.World, x, y - 1, z, 0, @event.BlockId)) && y > 0)
            {
                --y;
            }

            if (y > 0)
            {
                @event.World.Writer.SetBlock(x, y, z, block.id);
            }
        }
    }

    public static bool canFallThrough(OnTickEvent ctx)
    {
        int blockId = ctx.World.Reader.GetBlockId(ctx.X, ctx.Y, ctx.Z);
        if (blockId == 0) return true;
        if (blockId == Block.Fire.id) return true;

        Material material = Block.Blocks[blockId].material;
        return material == Material.Water || material == Material.Lava;
    }
}
