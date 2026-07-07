using BetaSharp.Entities;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
/// Cake: eating (right-click or left-click-start, both heal 3 and advance the slice counter)
/// shrinks the collision/visible box from the west edge as slices are eaten, and it wilts when
/// the block below is no longer solid.
/// </summary>
internal sealed class CakeBehavior : IBlockPhysics, IBlockVisuals, IBlockInteractable
{
    private const float CakeHeight = 0.5F;
    private const float EdgeInset = 1.0F / 16.0F;

    // ── IBlockPhysics ─────────────────────────────────────────────

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        int slicesEaten = reader.GetBlockMeta(x, y, z);
        float minX = (1 + slicesEaten * 2) / 16.0F;
        block.setBoundingBox(minX, 0.0F, EdgeInset, 1.0F - EdgeInset, CakeHeight, 1.0F - EdgeInset);
    }

    public void SetupRenderBoundingBox(Block block) => block.setBoundingBox(EdgeInset, 0.0F, EdgeInset, 1.0F - EdgeInset, CakeHeight, 1.0F - EdgeInset);

    public Box? GetCollisionShape(Block block, IBlockReader reader, EntityManager entities, int x, int y, int z, Box? defaultShape)
    {
        int slicesEaten = reader.GetBlockMeta(x, y, z);
        float minX = (1 + slicesEaten * 2) / 16.0F;
        return new Box(x + minX, y, z + EdgeInset, x + 1 - EdgeInset, y + CakeHeight - EdgeInset, z + 1 - EdgeInset);
    }

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event) => CanGrow(@event.World.Reader, @event.X, @event.Y, @event.Z);

    public bool CanGrow(Block block, OnTickEvent @event) => CanGrow(@event.World.Reader, @event.X, @event.Y, @event.Z);

    private static bool CanGrow(IBlockReader world, int x, int y, int z) => world.GetMaterial(x, y - 1, z).IsSolid;

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (CanGrow(@event.World.Reader, @event.X, @event.Y, @event.Z)) return;

        block.dropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)));
        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
    }

    // ── IBlockVisuals ─────────────────────────────────────────────

    public int GetTexture(Block block, Side side, int meta, int defaultTexture)
    {
        if (side == Side.Up) return BlockTextures.Cake;
        if (side == Side.Down) return block.TextureId + 3;
        if (meta > 0 && side == Side.West) return block.TextureId + 2;
        return block.TextureId + 1;
    }

    public int GetTexture(Block block, Side side, int defaultTexture) => side switch
    {
        Side.Up => block.TextureId,
        Side.Down => block.TextureId + 3,
        _ => block.TextureId + 1
    };

    // ── IBlockInteractable ────────────────────────────────────────

    public bool OnUse(Block block, OnUseEvent @event)
    {
        Eat(@event.Player, @event.World, @event.X, @event.Y, @event.Z);
        return true;
    }

    public void OnBlockBreakStart(Block block, OnBlockBreakStartEvent @event) => Eat(@event.Player, @event.World, @event.X, @event.Y, @event.Z);

    private static void Eat(EntityPlayer player, IWorldContext world, int x, int y, int z)
    {
        if (player.Health >= 20) return;

        player.Heal(3);
        int slicesEaten = world.Reader.GetBlockMeta(x, y, z) + 1;
        if (slicesEaten >= 6)
        {
            world.Writer.SetBlock(x, y, z, 0);
        }
        else
        {
            world.Writer.SetBlockMeta(x, y, z, slicesEaten);
            world.Broadcaster.SetBlocksDirty(x, y, z);
        }
    }
}
