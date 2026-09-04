using OmniBlock.Blocks;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Network.Messages;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Server.Network;

/*
 * mining and miningX,Y,Z don't mean the block you are mining.
 * It's used in update when the player provided brake was invalid.
 *
 * Instead, failedMining is the currently pending mining operation.
 */

/// <summary>
///     Handles mining, placing, opening doors, and so on.
/// </summary>
public class ServerPlayerInteractionManager
{
    private readonly ServerWorld world;
    private int failedMiningStartTime;
    private int failedMiningX;
    private int failedMiningY;
    private int failedMiningZ;
    private bool mining;
    private float miningProgress = -1;
    private int miningX;
    private int miningY;
    private int miningZ;
    public EntityPlayer player;
    private int startMiningTime;
    private int tickCounter;

    public ServerPlayerInteractionManager(ServerWorld world) => this.world = world;

    public void update()
    {
        tickCounter++;
        if (mining)
        {
            var miningTicks = tickCounter - startMiningTime;
            var blockId = world.Reader.GetBlockId(miningX, miningY, miningZ);
            if (blockId != 0)
            {
                var block = world.Content.Blocks.GetByProtocolId(blockId);
                var breakProgress = block.GetHardness(player) * (miningTicks + 1);
                if (breakProgress >= player.GameMode.BreakSpeed)
                {
                    mining = false;
                    miningProgress = -1;
                    tryBreakBlock(miningX, miningY, miningZ);
                }
            }
            else
            {
                mining = false;
                miningProgress = -1;
            }
        }
    }

    public void onBlockBreakingAction(int x, int y, int z, int direction)
    {
        if (player.GameMode.CanExhaustFire)
        {
            world.ExtinguishFire(null, x, y, z, direction);
        }

        failedMiningStartTime = tickCounter;
        var blockId = world.Reader.GetBlockId(x, y, z);
        if (blockId > 0 && player.GameMode.CanInteract)
        {
            world.Content.Blocks.GetByProtocolId(blockId).OnBlockBreakStart(new OnBlockBreakStartEvent(world, player, x, y, z));
        }

        if (!player.GameMode.CanBreak) return;
        if (blockId > 0 && world.Content.Blocks.GetByProtocolId(blockId).GetHardness(player) >= player.GameMode.BreakSpeed)
        {
            tryBreakBlock(x, y, z);
            miningProgress = -1;
        }
        else
        {
            failedMiningX = x;
            failedMiningY = y;
            failedMiningZ = z;
            miningProgress = 0.3f;
        }
    }

    public void continueMining(int x, int y, int z)
    {
        if (x == failedMiningX && y == failedMiningY && z == failedMiningZ)
        {
            var ticksSinceFailedStart = tickCounter - failedMiningStartTime;
            var blockId = world.Reader.GetBlockId(x, y, z);
            if (blockId != 0)
            {
                var block = world.Content.Blocks.GetByProtocolId(blockId);
                var breakProgress = block.GetHardness(player) * (ticksSinceFailedStart + 1) + miningProgress;
                if (breakProgress >= player.GameMode.BreakSpeed)
                {
                    tryBreakBlock(x, y, z);
                    miningProgress = -1;
                }
                else if (!mining)
                {
                    // Player submitted block brake was not accepted.
                    // As the block was not mined, we will check in update for when the block should be broken.
                    mining = true;
                    miningX = x;
                    miningY = y;
                    miningZ = z;
                    startMiningTime = failedMiningStartTime;
                }
            }
        }
    }

    public bool finishMining(int x, int y, int z)
    {
        var block = world.Content.Blocks.GetByProtocolId(world.Reader.GetBlockId(x, y, z));
        var blockMeta = world.Reader.GetBlockMeta(x, y, z);
        var success = world.Writer.SetBlock(x, y, z, 0);
        if (block != null && success)
        {
            block.OnMetadataChange(new OnMetadataChangeEvent(world, x, y, z, blockMeta));
        }

        return success;
    }


    public void UpdateMiningTool()
    {
        if (miningProgress is < 0F or >= 1F) return;
        var blockId = world.Reader.GetBlockId(failedMiningX, failedMiningY, failedMiningZ);
        if (blockId == 0)
        {
            miningProgress = -1;
            return;
        }

        var ticksSinceFailedStart = tickCounter - failedMiningStartTime;
        failedMiningStartTime = tickCounter;
        var block = world.Content.Blocks.GetByProtocolId(blockId);
        miningProgress += block.GetHardness(player) * ticksSinceFailedStart;
    }

    public bool tryBreakBlock(int x, int y, int z)
    {
        if (!player.GameMode.CanBreak) return false;

        var blockId = world.Reader.GetBlockId(x, y, z);
        var blockMeta = world.Reader.GetBlockMeta(x, y, z);
        world.Broadcaster.WorldEvent(player, 2001, x, y, z, blockId + blockMeta * 256);

        world.Content.Blocks.TryGetByProtocolId(blockId, out var block);
        var success = world.Writer.SetBlock(x, y, z, 0);
        if (block != null && success)
        {
            block.OnMetadataChange(new OnMetadataChangeEvent(world, x, y, z, blockMeta));

            if (player.GameMode.BlockDrops && player.CanHarvest(block))
            {
                block.OnAfterBreak(new OnAfterBreakEvent(world, player, blockMeta, x, y, z));
                ((ServerPlayerEntity)player).NetworkHandler.SendMessage(new BlockUpdateMessage
                {
                    X = x,
                    Y = (sbyte)y,
                    Z = z,
                    BlockRawId = (byte)world.Reader.GetBlockId(x, y, z),
                    BlockMetadata = (byte)world.Reader.GetBlockMeta(x, y, z)
                });
            }
        }

        var itemStack = player.GetHand();
        if (itemStack != null)
        {
            itemStack.PostMine(blockId, x, y, z, player);
            if (itemStack.Count == 0)
            {
                ItemStack.OnRemoved(player);
                player.ClearStackInHand();
            }
        }

        return success;
    }

    public bool interactItem(EntityPlayer player, IWorldContext world, ItemStack stack)
    {
        var count = stack.Count;
        var itemStack = stack.Use(world, player);
        if (itemStack != stack || (itemStack != null && itemStack.Count != count))
        {
            player.Inventory.Main[player.Inventory.SelectedSlot] = itemStack;
            if (itemStack.Count == 0)
            {
                player.Inventory.Main[player.Inventory.SelectedSlot] = null;
            }

            miningProgress = -1;

            return true;
        }

        return false;
    }

    public bool interactBlock(EntityPlayer player, World world, ItemStack? stack, int x, int y, int z, int side)
    {
        if (!player.IsSneaking())
        {
            if (!player.GameMode.CanInteract) return false;
            var blockId = world.Reader.GetBlockId(x, y, z);
            if (blockId > 0 && world.Content.Blocks.GetByProtocolId(blockId).OnUse(new OnUseEvent(world, player, x, y, z)))
            {
                miningProgress = -1;
                return true;
            }
        }

        if (stack == null || !player.GameMode.CanPlace) return false;
        if (stack.useOnBlock(player, world, x, y, z, side))
        {
            miningProgress = -1;
            return true;
        }

        return false;
    }
}
