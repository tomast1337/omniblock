using BetaSharp.Entities;
using BetaSharp.Items;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Snow layer: metadata 0-7 is height (2/16 per layer), requires an opaque/movement-blocking
///     surface below, melts in full block-light, and drops a snowball only when tool-mined —
///     support collapse and light-melt both go through the standard (zero-count) drop path, which
///     vanilla-accurately drops nothing.
///     <para>
///         Drop item (<paramref name="dropItem" />) is a required, JSON-declared constructor
///         param (see <c>BehaviorRegistry</c>'s <c>"snow"</c> entry) — no built-in vanilla
///         fallback; an omitted or unknown name throws immediately at startup.
///     </para>
/// </summary>
internal sealed class SnowBehavior(Item dropItem) : IBlockPhysics, IBlockTicker, IBlockLifecycle, IBlockVisuals
{
    private const float DropSpread = 0.7F;

    public void OnAfterBreak(Block block, OnAfterBreakEvent @event)
    {
        double offsetX = Random.Shared.NextSingle() * DropSpread + (1.0F - DropSpread) * 0.5D;
        double offsetY = Random.Shared.NextSingle() * DropSpread + (1.0F - DropSpread) * 0.5D;
        double offsetZ = Random.Shared.NextSingle() * DropSpread + (1.0F - DropSpread) * 0.5D;
        EntityItem entityItem = new(@event.World, @event.X + offsetX, @event.Y + offsetY, @event.Z + offsetZ, new ItemStack(dropItem.Id, 1, 0))
        {
            DelayBeforeCanPickup = 10
        };
        @event.World.Entities.SpawnEntity(entityItem);
        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
        @event.Player.IncreaseStat(Stats.Stats.MineBlockStatArray[block.id], 1);
    }

    public int GetDroppedItemId(Block block, int blockMeta, int defaultItemId) => dropItem.Id;

    public int GetDroppedItemCount(Block block, int defaultCount) => 0;

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        int meta = reader.GetBlockMeta(x, y, z) & 7;
        float height = 2 * (1 + meta) / 16.0F;
        block.SetBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, height, 1.0F);
    }

    public Box? GetCollisionShape(Block block, IBlockReader reader, EntityManager entities, int x, int y, int z, Box? defaultShape)
    {
        int meta = reader.GetBlockMeta(x, y, z) & 7;
        if (meta < 3) return null;

        return new Box(x + block.BoundingBox.MinX, y + block.BoundingBox.MinY, z + block.BoundingBox.MinZ, x + block.BoundingBox.MaxX, y + 0.5F, z + block.BoundingBox.MaxZ);
    }

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event)
    {
        int blockBelowId = @event.World.Reader.GetBlockId(@event.X, @event.Y - 1, @event.Z);
        return blockBelowId != 0 && Block.Blocks[blockBelowId].IsOpaque && @event.World.Reader.GetMaterial(@event.X, @event.Y - 1, @event.Z).BlocksMovement;
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
        => side == Side.Up || defaultVisibility;
}
