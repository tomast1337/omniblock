using BetaSharp.Entities;
using BetaSharp.Items;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>TNT: ignites from redstone power (on place or neighbor update) or a flint-and-steel break-start.</summary>
internal sealed class TNTBehavior : IBlockPhysics, IBlockLifecycle, IBlockInteractable, IBlockVisuals
{
    public int GetTexture(Block block, Side side, int defaultTexture) => side switch
    {
        Side.Down => BlockTextures.TntBottom,
        Side.Up => BlockTextures.TntTop,
        _ => BlockTextures.TntSide
    };

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (@event.BlockId <= 0 || !Block.Blocks[@event.BlockId].canEmitRedstonePower() || !@event.World.Redstone.IsPowered(@event.X, @event.Y, @event.Z))
        {
            return;
        }

        Ignite(block, @event.World, @event.X, @event.Y, @event.Z);
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
            Block.dropStack(@event.World, @event.X, @event.Y, @event.Z, new ItemStack(Block.TNT.id, 1, 0));
        }
        else
        {
            EntityTntPrimed entityTntPrimed = new(@event.World, @event.X + 0.5F, @event.Y + 0.5F, @event.Z + 0.5F);
            @event.World.Entities.SpawnEntity(entityTntPrimed);
            @event.World.Broadcaster.PlaySoundAtPos(@event.X + 0.5F, @event.Y + 0.5F, @event.Z + 0.5F, "random.fuse", 1.0F, 1.0F);
        }
    }

    public void OnBlockBreakStart(Block block, OnBlockBreakStartEvent @event)
    {
        if (@event.Player.GetHand() != null && @event.Player.GetHand().ItemId == Item.ByName("flint_and_steel").Id)
        {
            @event.World.Writer.SetBlockMetaWithoutNotifyingNeighbors(@event.X, @event.Y, @event.Z, 1);
        }
    }

    public void OnDestroyedByExplosion(Block block, OnDestroyedByExplosionEvent @event)
    {
        EntityTntPrimed entityTntPrimed = new(@event.World, @event.X + 0.5F, @event.Y + 0.5F, @event.Z + 0.5F);
        entityTntPrimed.Fuse = @event.World.Random.NextInt(entityTntPrimed.Fuse / 4) + entityTntPrimed.Fuse / 8;
        @event.World.Entities.SpawnEntity(entityTntPrimed);
    }

    private void Ignite(Block block, IWorldContext world, int x, int y, int z)
    {
        OnMetadataChange(block, new OnMetadataChangeEvent(world, x, y, z, 1));
        world.Writer.SetBlock(x, y, z, 0);
    }
}
