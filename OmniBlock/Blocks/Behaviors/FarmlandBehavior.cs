using OmniBlock.Blocks.Materials;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Farmland: moisture-driven wetness (metadata 0-7) that decays without nearby water/rain and
///     reverts to dirt, entity trampling, and a fixed-shape collision box independent of the
///     slightly-recessed render bounding box.
///     <para>
///         The block it reverts to (<paramref name="revertBlock" />) and the crop that keeps it
///         wet (<paramref name="crop" />) are both required (see <c>BehaviorRegistry</c>'s
///         <c>"farmland"</c> entry. so a non-vanilla soil variant reads naturally.
///     </para>
///     <para>
///         Trample chance (<paramref name="trampleChanceOneIn" />, 1-in-N per step), tick chance
///         (<paramref name="tickChanceOneIn" />, 1-in-N per tick that moisture logic runs at all),
///         and nearby-water search radius (<paramref name="waterCheckRadius" />) are also
///         required.
///     </para>
/// </summary>
internal sealed class FarmlandBehavior(Block revertBlock, Block crop, int trampleChanceOneIn, int tickChanceOneIn, int waterCheckRadius, int wet, int dry, int side)
    : IBlockTicker, IBlockPhysics, IBlockInteractable, IBlockLifecycle, IBlockVisuals
{
    public void OnSteppedOn(Block block, OnEntityStepEvent @event)
    {
        if (Random.Shared.Next(trampleChanceOneIn) == 0) @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, revertBlock.Id);
    }

    public int GetDroppedItemId(Block block, int blockMeta, int defaultItemId)
    {
        return revertBlock.GetDroppedItemId(0);
    }

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (@event.World.Reader.GetMaterial(@event.X, @event.Y + 1, @event.Z).IsSolid) @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, revertBlock.Id);
    }

    public Box? GetCollisionShape(Block block, IBlockReader reader, EntityManager entities, int x, int y, int z, Box? defaultShape)
    {
        return new Box(x, y, z, x + 1, y + 1, z + 1);
    }

    public void OnTick(Block block, OnTickEvent @event)
    {
        if (Random.Shared.Next(tickChanceOneIn) != 0) return;

        if (!IsWaterNearby(@event.World.Reader, @event.X, @event.Y, @event.Z) && !@event.World.Environment.IsRaining)
        {
            var meta = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
            if (meta > 0)
                @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, meta - 1);
            else if (!HasCrop(@event.World.Reader, @event.X, @event.Y, @event.Z)) @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, revertBlock.Id);
        }
        else
        {
            @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, 7);
        }
    }

    public int GetTexture(Block block, Side renderSide, int meta, int defaultTexture)
    {
        return renderSide switch
        {
            Side.Up when meta > 0 => wet,
            Side.Up => dry,
            _ => side
        };
    }

    private bool HasCrop(IBlockReader world, int x, int y, int z)
    {
        for (var dx = x - 0; dx <= x + 0; ++dx)
        for (var dy = z - 0; dy <= z + 0; ++dy)
            if (world.GetBlockId(dx, y + 1, dy) == crop.Id)
                return true;

        return false;
    }

    private bool IsWaterNearby(IBlockReader reader, int x, int y, int z)
    {
        for (var checkX = x - waterCheckRadius; checkX <= x + waterCheckRadius; ++checkX)
        for (var checkY = y; checkY <= y + 1; ++checkY)
        for (var checkZ = z - waterCheckRadius; checkZ <= z + waterCheckRadius; ++checkZ)
            if (reader.GetMaterial(checkX, checkY, checkZ) == Material.Water)
                return true;

        return false;
    }
}