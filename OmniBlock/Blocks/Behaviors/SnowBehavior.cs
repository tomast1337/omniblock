using OmniBlock.Entities.Behaviors;
using OmniBlock.Items;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     Snow layer: metadata 0-7 is height (2/16 per layer), requires an opaque/movement-blocking
///     surface below, melts in full block-light, and drops a snowball only when tool-mined,
///     support collapse and light-melt both go through the standard (zero-count) drop path, which
///     vanilla-accurately drops nothing.
///     <para>
///         Drop item (<paramref name="dropItem" />) is a required, (see <c>BehaviorRegistry</c>'s <c>"snow"</c> entry).
///     </para>
///     <para>
///         Drop spread (<paramref name="dropSpread" />) is also a required.
///     </para>
/// </summary>
internal sealed class SnowBehavior(Item dropItem, float dropSpread) : BlockRuntimeBehavior, IBlockPhysics, IBlockTicker, IBlockLifecycle, IBlockVisuals
{
    public void OnAfterBreak(Block block, OnAfterBreakEvent @event)
    {
        var offsetX = Random.Shared.NextSingle() * dropSpread + (1.0F - dropSpread) * 0.5D;
        var offsetY = Random.Shared.NextSingle() * dropSpread + (1.0F - dropSpread) * 0.5D;
        var offsetZ = Random.Shared.NextSingle() * dropSpread + (1.0F - dropSpread) * 0.5D;
        var entityItem = DroppedItemBehavior.Create(@event.World, @event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ, new ItemStack(dropItem, 1, 0), 10);
        @event.World.Entities.SpawnEntity(entityItem);
        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        @event.Player.IncreaseStat(Stats.Stats.MineBlockStatArray[block.Id], 1);
    }

    public int GetDroppedItemId(Block block, int blockMeta, int defaultItemId)
    {
        return dropItem.Id;
    }

    public int GetDroppedItemCount(Block block, int defaultCount)
    {
        return 0;
    }

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        var meta = reader.GetBlockMeta(x, y, z) & 7;
        var height = 2 * (1 + meta) / 16.0F;
        block.SetRuntimeBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, height, 1.0F);
    }

    public Box? GetCollisionShape(Block block, IBlockReader reader, EntityManager entities, int x, int y, int z, Box? defaultShape)
    {
        var meta = reader.GetBlockMeta(x, y, z) & 7;
        if (meta < 3) return null;

        return new Box(x + block.BoundingBox.MinX, y + block.BoundingBox.MinY, z + block.BoundingBox.MinZ, x + block.BoundingBox.MaxX, y + 0.5F, z + block.BoundingBox.MaxZ);
    }

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event)
    {
        var blockBelowId = @event.World.Reader.GetBlockId(@event.X, @event.Y - 1, @event.Z);
        return blockBelowId != 0 && Blocks.GetByProtocolId(blockBelowId).IsOpaque && @event.World.Reader.GetMaterial(@event.X, @event.Y - 1, @event.Z).BlocksMovement;
    }

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (CanPlaceAt(block, new CanPlaceAtContext(@event.World, 0, @event.X, @event.Y, @event.Z))) return;

        block.DropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)));
        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
    }

    public void OnTick(Block block, OnTickEvent @event)
    {
        if (@event.World.Lighting.GetBrightness(LightType.Block, @event.X, @event.Y, @event.Z) <= 11) return;
        block.DropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)));
        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
    }

    public bool IsSideVisible(Block block, IBlockReader reader, int x, int y, int z, Side side, bool defaultVisibility)
    {
        return side == Side.Up || defaultVisibility;
    }
}
