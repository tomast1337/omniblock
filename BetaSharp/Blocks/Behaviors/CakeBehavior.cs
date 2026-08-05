using BetaSharp.Entities;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Cake: eating (right-click or left-click-start, both heal 3 and advance the slice counter)
///     shrinks the collision/visible box from the west edge as slices are eaten, and it wilts when
///     the block below is no longer solid.
/// </summary>
internal sealed class CakeBehavior(int top, int side, int inner, int bottom) : IBlockPhysics, IBlockVisuals, IBlockInteractable
{
    private const float CakeHeight = 0.5F;
    private const float EdgeInset = 1.0F / 16.0F;

    public bool OnUse(Block block, OnUseEvent @event)
    {
        Eat(@event.Player, @event.World, @event.X, @event.Y, @event.Z);
        return true;
    }

    public void OnBlockBreakStart(Block block, OnBlockBreakStartEvent @event) => Eat(@event.Player, @event.World, @event.X, @event.Y, @event.Z);

    public void UpdateBoundingBox(Block block, IBlockReader reader, int x, int y, int z)
    {
        int slicesEaten = reader.GetBlockMeta(x, y, z);
        float minX = (1 + slicesEaten * 2) / 16.0F;
        block.SetBoundingBox(minX, 0.0F, EdgeInset, 1.0F - EdgeInset, CakeHeight, 1.0F - EdgeInset);
    }

    public void SetupRenderBoundingBox(Block block) => block.SetBoundingBox(EdgeInset, 0.0F, EdgeInset, 1.0F - EdgeInset, CakeHeight, 1.0F - EdgeInset);

    public Box? GetCollisionShape(Block block, IBlockReader reader, EntityManager entities, int x, int y, int z, Box? defaultShape)
    {
        int slicesEaten = reader.GetBlockMeta(x, y, z);
        float minX = (1 + slicesEaten * 2) / 16.0F;
        return new Box(x + minX, y, z + EdgeInset, x + 1 - EdgeInset, y + CakeHeight - EdgeInset, z + 1 - EdgeInset);
    }

    public bool CanPlaceAt(Block block, CanPlaceAtContext @event) => CanGrow(@event.World.Reader, @event.X, @event.Y, @event.Z);

    public bool CanGrow(Block block, OnTickEvent @event) => CanGrow(@event.World.Reader, @event.X, @event.Y, @event.Z);

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (CanGrow(@event.World.Reader, @event.X, @event.Y, @event.Z)) return;

        block.DropStacks(new OnDropEvent(@event.World, @event.X, @event.Y, @event.Z, @event.World.Reader.GetBlockMeta(@event.X, @event.Y, @event.Z)));
        @event.World.Writer.SetBlock(@event.X, @event.Y, @event.Z, 0);
    }

    public int GetTexture(Block block, Side renderSide, int meta, int defaultTexture)
    {
        if (renderSide == Side.Up) return top;
        if (renderSide == Side.Down) return bottom;
        // The cut face, exposed only once a slice is gone.
        if (meta > 0 && renderSide == Side.West) return inner;
        return side;
    }

    public int GetTexture(Block block, Side renderSide, int defaultTexture) => renderSide switch
    {
        Side.Up => top,
        Side.Down => bottom,
        _ => side
    };

    private static bool CanGrow(IBlockReader world, int x, int y, int z) => world.GetMaterial(x, y - 1, z).IsSolid;

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
