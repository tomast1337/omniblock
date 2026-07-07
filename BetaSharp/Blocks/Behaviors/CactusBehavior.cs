using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Cactus: vertical growth up to 3 tall (metadata 0-15 counts ticks toward the next segment),
/// requiring clear horizontal neighbors and sand/self below, plus 1-damage collision with
/// entities. The render bounding box is full-height (declarative, set on the static); the
/// collision box is one pixel shorter on every side via <see cref="GetCollisionShape"/>.
/// </summary>
internal sealed class CactusBehavior : IBlockTicker, IBlockPhysics, IBlockInteractable, IBlockVisuals
{
    private const float EdgeInset = 1.0F / 16.0F;

    // ── IBlockTicker ──────────────────────────────────────────────

    public void OnTick(Block block, OnTickEvent @event)
    {
        if (!@event.World.Reader.IsAir(@event.X, @event.Y + 1, @event.Z)) return;

        int heightBelow = 1;
        while (@event.World.Reader.GetBlockId(@event.X, @event.Y - heightBelow, @event.Z) == block.id) heightBelow++;

        if (heightBelow >= 3) return;

        int growthStage = @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z);
        if (growthStage == 15)
        {
            @event.World.Writer.SetBlock(@event.X, @event.Y + 1, @event.Z, block.id);
            @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, 0);
        }
        else
        {
            @event.World.Writer.SetBlockMeta(@event.X, @event.Y, @event.Z, growthStage + 1);
        }
    }

    // ── IBlockPhysics ─────────────────────────────────────────────

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event)
        => CanGrowAt(@event.World.Reader, @event.X, @event.Y, @event.Z);

    public bool CanGrow(Block block, OnTickEvent @event)
        => CanGrowAt(@event.World.Reader, @event.X, @event.Y, @event.Z);

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (CanGrowAt(@event.World.Reader, @event.X, @event.Y, @event.Z)) return;

        block.dropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)));
        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
    }

    private static bool CanGrowAt(IBlockReader world, int x, int y, int z)
    {
        if (world.GetMaterial(x - 1, y, z).IsSolid) return false;
        if (world.GetMaterial(x + 1, y, z).IsSolid) return false;
        if (world.GetMaterial(x, y, z - 1).IsSolid) return false;
        if (world.GetMaterial(x, y, z + 1).IsSolid) return false;
        int blockBelowId = world.GetBlockId(x, y - 1, z);
        return blockBelowId == Block.Cactus.id || blockBelowId == Block.Sand.id;
    }

    public Box? GetCollisionShape(Block block, IBlockReader reader, EntityManager entities, int x, int y, int z, Box? defaultShape)
        => new Box(x + EdgeInset, y, z + EdgeInset, x + 1 - EdgeInset, y + 1 - EdgeInset, z + 1 - EdgeInset);

    // ── IBlockInteractable ────────────────────────────────────────

    public void OnEntityCollision(Block block, OnEntityCollisionEvent @event) => @event.Entity.Damage(null, 1);

    // ── IBlockVisuals ─────────────────────────────────────────────

    public int GetTexture(Block block, Side side, int defaultTexture) => side switch
    {
        Side.Up => BlockTextures.CactusTop,
        Side.Down => BlockTextures.CactusBottom,
        _ => BlockTextures.CactusSide
    };
}
