using OmniBlock.Blocks;
using OmniBlock.Client.Entities;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Client.Input;

public class PlayerController
{
    protected readonly OmniBlock Game;
    public bool IsTestPlayer = false;

    public PlayerController(OmniBlock game) => Game = game;

    public virtual void ChangeWorld(World world)
    {
    }

    public virtual void ClickBlock(int x, int y, int z, int direction)
    {
        Game.World.ExtinguishFire(Game.Player, x, y, z, direction);
        SendBlockRemoved(x, y, z, direction);
    }

    public virtual bool SendBlockRemoved(int x, int y, int z, int direction)
    {
        var world = Game.World;
        var block = world.Content.Blocks.GetByProtocolId(world.Reader.GetBlockId(x, y, z));
        world.Broadcaster.NotifyNeighbors(x, y, z, world.Reader.GetBlockId(x, y, z));
        var blockMeta = world.Reader.GetBlockMeta(x, y, z);
        var success = world.Writer.SetBlock(x, y, z, 0);
        if (block != null && success)
        {
            block.OnMetadataChange(new OnMetadataChangeEvent(world, x, y, z, blockMeta));
        }

        return success;
    }

    public virtual void SendBlockRemoving(int x, int y, int z, int direction)
    {
    }

    public virtual void ResetBlockRemoving()
    {
    }

    public virtual void SetPartialTime(float tickDelta)
    {
    }

    public virtual float GetBlockReachDistance() => Game.Player.GameMode.BlockReach;
    public virtual float GetEntityReachDistance() => Game.Player.GameMode.EntityReach;

    public virtual bool SendUseItem(EntityPlayer player, World world, ItemStack stack)
    {
        var originalCount = stack.Count;
        var resultStack = stack.Use(world, player);
        if (resultStack != stack || (resultStack != null && resultStack.Count != originalCount))
        {
            player.Inventory.Main[player.Inventory.SelectedSlot] = resultStack;
            if (resultStack.Count == 0)
            {
                player.Inventory.Main[player.Inventory.SelectedSlot] = null;
            }

            return true;
        }

        return false;
    }

    public virtual void FlipPlayer(EntityPlayer playerEntity)
    {
    }

    public virtual void UpdateController()
    {
    }

    public virtual bool ShouldDrawHUD() => true;

    public virtual void FillHotbar(EntityPlayer player)
    {
    }

    public virtual bool SendPlaceBlock(
        ClientPlayerEntity player,
        IWorldContext world,
        ItemStack selectedItem,
        int blockX,
        int blockY,
        int blockZ,
        int blockSide
    )
    {
        var targetId = world.Reader.GetBlockId(blockX, blockY, blockZ);

        if (targetId > 0 && !player.IsSneaking())
        {
            if (!player.GameMode.CanInteract) return false;
            var used = world.Content.Blocks.GetByProtocolId(targetId).OnUse(new OnUseEvent(world, player, blockX, blockY, blockZ));
            if (used) return true;
        }

        if (selectedItem == null || !player.GameMode.CanPlace) return false;

        return selectedItem.useOnBlock(player, world, blockX, blockY, blockZ, blockSide);
    }

    public virtual EntityPlayer CreatePlayer(World world) =>
        new ClientPlayerEntity(Game, world, Game.Session, world.Dimension.Id);

    public virtual void InteractWithEntity(EntityPlayer player, Entity target) =>
        player.Interact(target);

    public virtual void AttackEntity(EntityPlayer player, Entity target) =>
        player.Attack(target);

    public virtual ItemStack OnSlotClick(int windowId, int slotIndex, int mouseButton, bool shiftClick, EntityPlayer player) => player.CurrentScreenHandler.onSlotClick(slotIndex, mouseButton, shiftClick, player);

    public virtual void OnGuiClosed(int windowId, EntityPlayer player)
    {
        player.CurrentScreenHandler.onClosed(player);
        player.CurrentScreenHandler = player.PlayerScreenHandler;
    }
}
