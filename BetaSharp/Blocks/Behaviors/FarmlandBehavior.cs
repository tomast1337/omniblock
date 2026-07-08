using BetaSharp.Blocks.Materials;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Farmland: moisture-driven wetness (metadata 0-7) that decays without nearby water/rain and
///     reverts to dirt, entity trampling, and a fixed-shape collision box independent of the
///     slightly-recessed render bounding box.
/// </summary>
internal sealed class FarmlandBehavior : IBlockTicker, IBlockPhysics, IBlockInteractable, IBlockLifecycle, IBlockVisuals
{
    public void OnSteppedOn(Block block, OnEntityStepEvent @event)
    {
        if (Random.Shared.Next(4) == 0)
        {
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, BlockRegistry.Get("dirt").Id);
        }
    }

    public int GetDroppedItemId(Block block, int blockMeta, int defaultItemId) => BlockRegistry.Get("dirt").GetDroppedItemId(0);

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (@event.World.Reader.GetMaterial(@event.X, @event.Y + 1, @event.Z).IsSolid)
        {
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, BlockRegistry.Get("dirt").Id);
        }
    }

    public Box? GetCollisionShape(Block block, IBlockReader reader, EntityManager entities, int x, int y, int z, Box? defaultShape)
        => new Box(x, y, z, x + 1, y + 1, z + 1);

    public void OnTick(Block block, OnTickEvent @event)
    {
        if (Random.Shared.Next(5) != 0) return;

        if (!IsWaterNearby(@event.World.Reader, @event.X, @event.Y, @event.Z) && !@event.World.Environment.IsRaining)
        {
            int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
            if (meta > 0)
            {
                @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, meta - 1);
            }
            else if (!HasCrop(@event.World.Reader, @event.X, @event.Y, @event.Z))
            {
                @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, BlockRegistry.Get("dirt").Id);
            }
        }
        else
        {
            @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, 7);
        }
    }

    public int GetTexture(Block block, Side side, int meta, int defaultTexture) => side switch
    {
        Side.Up when meta > 0 => BlockTextures.FarmlandWet,
        Side.Up => BlockTextures.FarmlandDry,
        _ => BlockTextures.Dirt
    };

    private static bool HasCrop(IBlockReader world, int x, int y, int z)
    {
        for (int dx = x - 0; dx <= x + 0; ++dx)
        {
            for (int dy = z - 0; dy <= z + 0; ++dy)
            {
                if (world.GetBlockId(dx, y + 1, dy) == BlockRegistry.Get("wheat").Id) return true;
            }
        }

        return false;
    }

    private static bool IsWaterNearby(IBlockReader reader, int x, int y, int z)
    {
        for (int checkX = x - 4; checkX <= x + 4; ++checkX)
        {
            for (int checkY = y; checkY <= y + 1; ++checkY)
            {
                for (int checkZ = z - 4; checkZ <= z + 4; ++checkZ)
                {
                    if (reader.GetMaterial(checkX, checkY, checkZ) == Material.Water)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
