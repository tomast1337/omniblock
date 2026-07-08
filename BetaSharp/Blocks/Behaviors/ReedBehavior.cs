using BetaSharp.Blocks.Materials;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Sugar cane: vertical growth up to 3 tall (metadata 0-15 counts ticks toward the next
///     segment), requiring valid ground adjacent to water. The ground+water check is a private
///     helper reused directly by <see cref="CanGrow" />/<see cref="NeighborUpdate" /> rather than
///     routed through the full <c>canPlaceAt</c> dispatch — that dispatch ANDs with the base
///     replaceability check, which is false for the reed's own (non-replaceable) material and would
///     make the break-recheck always fail.
/// </summary>
internal sealed class ReedBehavior : IBlockTicker, IBlockPhysics
{
    public bool CanPlaceAt(Block block, CanPlaceAtContext @event)
        => CanSurviveAt(@event.World.Reader, block.Id, @event.X, @event.Y, @event.Z);

    public bool CanGrow(Block block, OnTickEvent @event)
        => CanSurviveAt(@event.World.Reader, block.Id, @event.X, @event.Y, @event.Z);

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (CanSurviveAt(@event.World.Reader, block.Id, @event.X, @event.Y, @event.Z)) return;

        block.DropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)));
        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
    }
    public void OnTick(Block block, OnTickEvent @event)
    {
        if (!@event.World.Reader.IsAir(@event.X, @event.Y + 1, @event.Z)) return;

        int heightBelow = 1;
        while (@event.World.Reader.GetBlockId(@event.X, @event.Y - heightBelow, @event.Z) == block.Id)
        {
            heightBelow++;
        }

        if (heightBelow >= 3) return;

        int meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        if (meta == 15)
        {
            @event.World.Writer.SetBlock(@event.X, @event.Y + 1, @event.Z, block.Id);
            @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, 0);
        }
        else
        {
            @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, meta + 1);
        }
    }

    private static bool CanSurviveAt(IBlockReader reader, int selfId, int x, int y, int z)
    {
        int blockBelowId = reader.GetBlockId(x, y - 1, z);

        // Can always place on self.
        if (blockBelowId == selfId) return true;

        // Valid block below.
        if (blockBelowId != BlockRegistry.Get("grass_block").Id &&
            blockBelowId != BlockRegistry.Get("dirt").Id &&
            blockBelowId != BlockRegistry.Get("sand").Id &&
            blockBelowId != BlockRegistry.Get("gravel").Id) return false;

        // Water adjacent.
        return reader.GetMaterial(x - 1, y - 1, z) == Material.Water ||
               reader.GetMaterial(x + 1, y - 1, z) == Material.Water ||
               reader.GetMaterial(x, y - 1, z - 1) == Material.Water ||
               reader.GetMaterial(x, y - 1, z + 1) == Material.Water;
    }
}
