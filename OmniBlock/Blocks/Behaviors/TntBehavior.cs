using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Items;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Blocks.Behaviors;

/// <summary>
///     TNT: ignites from redstone power (on place or neighbor update) or an igniter-tool
///     break-start.
///     <para>
///         Igniter tool item is a required, (see <c>BehaviorRegistry</c>'s <c>"tnt"</c> entry).
///     </para>
/// </summary>
internal sealed class TntBehavior(Item igniter, int top, int side, int bottom) : BlockRuntimeBehavior, IBlockPhysics, IBlockLifecycle, IBlockInteractable, IBlockVisuals
{
    public void OnBlockBreakStart(Block block, OnBlockBreakStartEvent @event)
    {
        if (@event.Player.GetHand() != null && @event.Player.GetHand()!.ItemId == igniter.Id)
        {
            @event.World.Writer.SetBlockMetaWithoutNotifyingNeighbors(@event.X, @event.Y, @event.Z, 1);
        }
    }

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (!@event.World.Redstone.IsPowered(@event.X, @event.Y, @event.Z)) return;
        Ignite(block, @event.World, @event.X, @event.Y, @event.Z);
    }

    public void OnMetadataChange(Block block, OnMetadataChangeEvent @event)
    {
        if (@event.World.IsRemote) return;

        if ((@event.Meta & 1) == 0)
        {
            Block.DropStack(@event.World, @event.X, @event.Y, @event.Z, new ItemStack(block.Id, 1, 0));
        }
        else
        {
            SpawnPrimed(@event.World, @event.X, @event.Y, @event.Z);
            @event.World.Broadcaster.PlaySoundAtPos(@event.X + 0.5F, @event.Y + 0.5F, @event.Z + 0.5F, "random.fuse", 1.0F, 1.0F);
        }
    }

    public void OnDestroyedByExplosion(Block block, OnDestroyedByExplosionEvent @event)
    {
        Entity primed = SpawnPrimed(@event.World, @event.X, @event.Y, @event.Z);
        primed.Behaviors.Find<PrimedExplosiveBehavior>()!.ShortenFuse(primed);
    }

    private static Entity SpawnPrimed(IWorldContext world, int x, int y, int z)
    {
        Entity primed = EntityRegistry.ByName("primedtnt").Create(world);
        primed.SetPositionAndAngles(x + 0.5F, y + 0.5F, z + 0.5F, 0.0F, 0.0F);
        world.Entities.SpawnEntity(primed);
        return primed;
    }

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (@event.BlockId <= 0 || !Blocks.GetByProtocolId(@event.BlockId).CanEmitRedstonePower() || !@event.World.Redstone.IsPowered(@event.X, @event.Y, @event.Z))
            return;

        Ignite(block, @event.World, @event.X, @event.Y, @event.Z);
    }

    public int GetTexture(Block block, Side renderSide, int defaultTexture) => renderSide switch
    {
        Side.Down => bottom,
        Side.Up => top,
        _ => side
    };

    private void Ignite(Block block, IWorldContext world, int x, int y, int z)
    {
        OnMetadataChange(block, new OnMetadataChangeEvent(world, x, y, z, 1));
        world.Writer.SetBlock(x, y, z, 0);
    }
}
