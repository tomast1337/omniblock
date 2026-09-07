using OmniBlock.Blocks;
using OmniBlock.Client.Entities;
using OmniBlock.Client.Network;
using OmniBlock.Client.Sound;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Network.Messages;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Client.Input;

public class PlayerControllerMP : PlayerController
{
    private readonly ClientNetworkHandler _netClientHandler;
    private int _blockHitDelay;
    private float _curBlockDamageMp;
    private int _currentPlayerItem;
    private bool _isHittingBlock;
    private byte _mineSoundTimer;
    private float _prevBlockDamageMp;
    private Vec3I _targetBlockPos;

    public PlayerControllerMP(OmniBlock game, ClientNetworkHandler networkHandler) : base(game) => _netClientHandler = networkHandler;

    public override void FlipPlayer(EntityPlayer playerEntity)
    {
        playerEntity.Yaw = -180.0F;
        playerEntity.PrevYaw = -180.0F;
    }

    public override bool SendBlockRemoved(int x, int y, int z, int direction)
    {
        if (!Game.Player.GameMode.CanBreak) return false;

        var blockId = Game.World.Reader.GetBlockId(x, y, z);
        var blockRemoved = base.SendBlockRemoved(x, y, z, direction);
        var hand = Game.Player.GetHand();
        if (hand != null)
        {
            hand.PostMine(blockId, x, y, z, Game.Player);
            if (hand.Count == 0)
            {
                ItemStack.OnRemoved(Game.Player);
                Game.Player.ClearStackInHand();
            }
        }

        return blockRemoved;
    }

    public override void ClickBlock(int x, int y, int z, int direction)
    {
        if (!_isHittingBlock || x != _targetBlockPos.X || y != _targetBlockPos.Y || z != _targetBlockPos.Z)
        {
            _netClientHandler.SendMessage(PlayerAction(PlayerActionMessage.Actions.BlockClick, x, y, z, direction));
            var blockId = Game.World.Reader.GetBlockId(x, y, z);
            if (blockId > 0 && _curBlockDamageMp == 0.0F && Game.Player.GameMode.CanInteract)
            {
                Game.Content.Blocks.GetByProtocolId(blockId).OnBlockBreakStart(new OnBlockBreakStartEvent(Game.World, Game.Player, x, y, z));
            }

            if (!Game.Player.GameMode.CanBreak) return;

            if (blockId > 0 && Game.Content.Blocks.GetByProtocolId(blockId).GetHardness(Game.Player) >= Game.Player.GameMode.BreakSpeed)
            {
                var meta = Game.World.Reader.GetBlockMeta(x, y, z);
                if (SendBlockRemoved(x, y, z, direction))
                {
                    Game.WorldRenderer.WorldEventBreak(blockId, meta, x, y, z);
                }
            }
            else
            {
                _isHittingBlock = true;
                _targetBlockPos = new Vec3I(x, y, z);
                _curBlockDamageMp = 0.0F;
                _prevBlockDamageMp = 0.0F;
                _mineSoundTimer = 0;
            }
        }
    }

    public override void ResetBlockRemoving()
    {
        _curBlockDamageMp = 0.0F;
        _isHittingBlock = false;
    }

    public override void SendBlockRemoving(int x, int y, int z, int direction)
    {
        if (_isHittingBlock)
        {
            SyncCurrentPlayItem();
            if (_blockHitDelay > 0)
            {
                --_blockHitDelay;
            }
            else
            {
                if (x == _targetBlockPos.X && y == _targetBlockPos.Y && z == _targetBlockPos.Z)
                {
                    if (!Game.Player.GameMode.CanBreak) return;

                    var blockId = Game.World.Reader.GetBlockId(x, y, z);
                    if (blockId == 0)
                    {
                        _isHittingBlock = false;
                        return;
                    }

                    var block = Game.Content.Blocks.GetByProtocolId(blockId);

                    // If it's an unknown block id, break behavior will be handled on server.
                    if (block == null)
                    {
                        if (_mineSoundTimer++ % 4 == 0)
                        {
                            Game.SoundManager.PlayStepSound(Game.Content.Blocks.Get("bedrock").SoundGroup, x, y, z);
                        }

                        return;
                    }

                    _curBlockDamageMp += block.GetHardness(Game.Player);
                    if (_mineSoundTimer++ % 4 == 0)
                    {
                        Game.SoundManager.PlayStepSound(block.SoundGroup, x, y, z);
                    }

                    if (_curBlockDamageMp >= 1.0F)
                    {
                        _isHittingBlock = false;
                        _netClientHandler.SendMessage(PlayerAction(PlayerActionMessage.Actions.BlockBroken, x, y, z, direction));
                        if (SendBlockRemoved(x, y, z, direction))
                        {
                            Game.WorldRenderer.WorldEventBreak(blockId, Game.World.Reader.GetBlockMeta(x, y, z), x, y, z);
                        }

                        _curBlockDamageMp = 0.0F;
                        _prevBlockDamageMp = 0.0F;
                        _mineSoundTimer = 0;
                        _blockHitDelay = 5;
                    }
                }
                else
                {
                    ClickBlock(x, y, z, direction);
                }
            }
        }
    }

    public override void SetPartialTime(float tickDelta)
    {
        if (_curBlockDamageMp <= 0.0F)
        {
            Game.WorldRenderer.DamagePartialTime = 0.0F;
        }
        else
        {
            var partialDamage = _prevBlockDamageMp + (_curBlockDamageMp - _prevBlockDamageMp) * tickDelta;
            Game.WorldRenderer.DamagePartialTime = partialDamage;
        }
    }

    public override void UpdateController()
    {
        SyncCurrentPlayItem();
        _prevBlockDamageMp = _curBlockDamageMp;
        Game.SoundManager.PlayRandomMusicIfReady(DefaultMusicCategories.Game);
    }

    private void SyncCurrentPlayItem()
    {
        var selectedSlot = Game.Player.Inventory.SelectedSlot;
        if (selectedSlot != _currentPlayerItem)
        {
            _currentPlayerItem = selectedSlot;
            _netClientHandler.SendMessage(new SelectedSlotMessage
            {
                Slot = (short)_currentPlayerItem
            });
        }
    }

    public override bool SendPlaceBlock(
        ClientPlayerEntity player,
        IWorldContext world,
        ItemStack selectedItem,
        int blockX,
        int blockY,
        int blockZ,
        int blockSide
    )
    {
        SyncCurrentPlayItem();
        _netClientHandler.SendMessage(InteractBlock(blockX, blockY, blockZ, blockSide, player.Inventory.ItemInHand));
        var placed = base.SendPlaceBlock(player, world, selectedItem, blockX, blockY, blockZ, blockSide);
        return placed;
    }

    public override bool SendUseItem(EntityPlayer player, World world, ItemStack stack)
    {
        SyncCurrentPlayItem();
        _netClientHandler.SendMessage(InteractBlock(-1, -1, -1, 255, player.Inventory.ItemInHand));
        var usedItem = base.SendUseItem(player, world, stack);
        return usedItem;
    }

    public override EntityPlayer CreatePlayer(World world) =>
        new EntityClientPlayerMP(Game, world, Game.Session, _netClientHandler);

    public override void AttackEntity(EntityPlayer player, Entity target)
    {
        SyncCurrentPlayItem();
        _netClientHandler.SendInteractEntity(player.ID, target.ID, 1);
        player.Attack(target);
    }

    public override void InteractWithEntity(EntityPlayer player, Entity target)
    {
        SyncCurrentPlayItem();
        _netClientHandler.SendInteractEntity(player.ID, target.ID, 0);
        player.Interact(target);
    }

    public override ItemStack OnSlotClick(int windowId, int slotIndex, int mouseButton, bool shiftClick, EntityPlayer player)
    {
        var revision = player.CurrentScreenHandler.nextRevision(player.Inventory);
        var resultStack = base.OnSlotClick(windowId, slotIndex, mouseButton, shiftClick, player);
        _netClientHandler.SendMessage(new ClickSlotMessage
        {
            SyncId = (sbyte)windowId,
            Slot = (short)slotIndex,
            Button = (sbyte)mouseButton,
            ActionType = revision,
            HoldingShift = shiftClick,
            Stack = resultStack
        });
        return resultStack;
    }

    public override void OnGuiClosed(int windowId, EntityPlayer player)
    {
        if (windowId != -9999)
        {
        }
    }

    /// <summary>
    ///     Y and the block face are bytes on the wire, and were already read as bytes before this
    ///     was a message — the world is 128 blocks tall and a face is one of six. The casts are
    ///     here rather than at each call site so the narrowing is stated once.
    /// </summary>
    private static PlayerActionMessage PlayerAction(
        PlayerActionMessage.Actions action, int x, int y, int z, int direction) => new()
    {
        Action = (byte)action,
        X = x,
        Y = (byte)y,
        Z = z,
        Direction = (byte)direction
    };

    /// <summary>Side 255 with x, y and z at -1 is the "used an item with no block in front" case.</summary>
    private static InteractBlockMessage InteractBlock(int x, int y, int z, int side, ItemStack? stack) => new()
    {
        X = x,
        Y = (byte)y,
        Z = z,
        Side = (byte)side,
        Stack = stack
    };
}
