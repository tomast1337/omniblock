using BetaSharp.Blocks.Entities;

namespace BetaSharp.Blocks.Behaviors;

/// <summary>
///     Note block: right-click cycles the note, left-click and redstone rising edges play it, and
///     the block action packet renders the sound and particle client-side. The tile entity itself
///     comes from the block's <c>setHasTileEntity</c> factory. Assign to the Interactable,
///     Lifecycle, and Physics slots.
/// </summary>
public sealed class NoteBlockBehavior : IBlockInteractable, IBlockLifecycle, IBlockPhysics
{
    public bool OnUse(Block block, OnUseEvent @event)
    {
        if (@event.World.IsRemote) return true;

        BlockEntityNote? blockEntity = @event.World.Entities.GetBlockEntity<BlockEntityNote>(@event.X, @event.Y, @event.Z);
        if (blockEntity == null) return false;

        blockEntity.CycleNote();
        blockEntity.PlayNote(@event.World, @event.X, @event.Y, @event.Z);
        return true;
    }

    public void OnBlockBreakStart(Block block, OnBlockBreakStartEvent @event)
    {
        if (@event.World.IsRemote) return;

        BlockEntityNote? blockEntity = @event.World.Entities.GetBlockEntity<BlockEntityNote>(@event.X, @event.Y, @event.Z);
        blockEntity?.PlayNote(@event.World, @event.X, @event.Y, @event.Z);
    }

    public void OnPlaced(Block block, OnPlacedEvent @event)
    {
        if (block.GetBlockEntity() is { } blockEntity)
        {
            @event.World.Entities.SetBlockEntity(@event.X, @event.Y, @event.Z, blockEntity);
        }
    }

    public void OnBreak(Block block, OnBreakEvent @event) => @event.World.Entities.RemoveBlockEntity(@event.X, @event.Y, @event.Z);

    public void OnBlockAction(Block block, OnBlockActionEvent @event)
    {
        float pitch = (float)Math.Pow(2.0D, (@event.Data2 - 12) / 12.0D);
        string instrumentName = @event.Data1 switch
        {
            1 => "bd",
            2 => "snare",
            3 => "hat",
            4 => "bassattack",
            _ => "harp"
        };

        @event.World.Broadcaster.PlaySoundAtPos(@event.X + 0.5D, @event.Y + 0.5D, @event.Z + 0.5D, "note." + instrumentName, 3.0F, pitch);
        @event.World.Broadcaster.AddParticle("note", @event.X + 0.5D, @event.Y + 1.2D, @event.Z + 0.5D, @event.Data2 / 24.0D, 0.0D, 0.0D);
    }

    public void NeighborUpdate(Block block, OnTickEvent @event)
    {
        if (!(@event.BlockId > 0 && Block.Blocks[@event.BlockId].CanEmitRedstonePower()))
        {
            return;
        }

        bool isPowered = @event.World.Redstone.IsStrongPowered(@event.X, @event.Y, @event.Z);
        BlockEntityNote? blockEntity = @event.World.Entities.GetBlockEntity<BlockEntityNote>(@event.X, @event.Y, @event.Z);
        if (blockEntity == null || blockEntity.powered == isPowered)
        {
            return;
        }

        if (isPowered)
        {
            blockEntity.PlayNote(@event.World, @event.X, @event.Y, @event.Z);
        }

        blockEntity.powered = isPowered;
    }
}
