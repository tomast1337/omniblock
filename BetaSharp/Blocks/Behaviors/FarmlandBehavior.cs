using BetaSharp.Blocks.Materials;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Farmland: moisture-driven wetness (metadata 0-7) that decays without nearby water/rain and
/// reverts to dirt, entity trampling, and a fixed-shape collision box independent of the
/// slightly-recessed render bounding box.
/// </summary>
internal sealed class FarmlandBehavior : IBlockTicker, IBlockPhysics, IBlockInteractable, IBlockLifecycle, IBlockVisuals
{
    private const sbyte CropRadius = 0;

    // ── IBlockTicker ──────────────────────────────────────────────

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
                @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, Block.Dirt.id);
            }
        }
        else
        {
            @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, 7);
        }
    }

    private static bool HasCrop(IBlockReader world, int x, int y, int z)
    {
        for (int dx = x - CropRadius; dx <= x + CropRadius; ++dx)
        {
            for (int dy = z - CropRadius; dy <= z + CropRadius; ++dy)
            {
                if (world.GetBlockId(dx, y + 1, dy) == Block.Wheat.id) return true;
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

    // ── IBlockInteractable ────────────────────────────────────────

    public void OnSteppedOn(Block block, OnEntityStepEvent @event)
    {
        if (Random.Shared.Next(4) == 0)
        {
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, Block.Dirt.id);
        }
    }

    // ── IBlockPhysics ─────────────────────────────────────────────

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (@event.World.Reader.GetMaterial(@event.X, @event.Y + 1, @event.Z).IsSolid)
        {
            @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, Block.Dirt.id);
        }
    }

    public Box? GetCollisionShape(Block block, IBlockReader reader, EntityManager entities, int x, int y, int z, Box? defaultShape)
        => new Box(x, y, z, x + 1, y + 1, z + 1);

    // ── IBlockVisuals ─────────────────────────────────────────────

    public int GetTexture(Block block, Side side, int meta, int defaultTexture) => side switch
    {
        Side.Up when meta > 0 => BlockTextures.FarmlandWet - 1,
        Side.Up => BlockTextures.FarmlandWet,
        _ => BlockTextures.Dirt
    };

    // ── IBlockLifecycle ───────────────────────────────────────────

    public int GetDroppedItemId(Block block, int blockMeta, int defaultItemId) => Block.Dirt.getDroppedItemId(0);
}
