using OmniBlock.Blocks;
using OmniBlock.Blocks.Entities;
using OmniBlock.Entities;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items;

internal class ItemBlock : Item
{
    private readonly IBlockEntityItemData? _itemData;
    protected readonly Block Block;

    public ItemBlock(Block block) : base(block.Id - 256)
    {
        Block = block;
        _itemData = block.GetBlockEntity() as IBlockEntityItemData;
        SetTextureId(block.GetTexture(2.ToSide()));
    }

    internal Block RuntimeBlock => Block;
    private int BlockId => Block.Id;

    public override bool useOnBlock(ItemStack itemStack, EntityPlayer entityPlayer, IWorldContext world, int x, int y, int z, int meta)
    {
        if (world.Reader.GetBlockId(x, y, z) == world.Content.Blocks.Get("snow").Id)
        {
            meta = 0;
        }
        else
        {
            switch (meta)
            {
                case 0:
                    --y;
                    break;
                case 1:
                    ++y;
                    break;
                case 2:
                    --z;
                    break;
                case 3:
                    ++z;
                    break;
                case 4:
                    --x;
                    break;
                case 5:
                    ++x;
                    break;
            }
        }

        if (itemStack.Count == 0)
        {
            return false;
        }

        var existingBlockId = world.Reader.GetBlockId(x, y, z);
        if (existingBlockId != 0 && !world.Content.Blocks.GetByProtocolId(existingBlockId).Material.IsReplaceable)
        {
            return false;
        }

        if (y >= ChuckFormat.WorldHeight)
        {
            return false;
        }

        var block = world.Content.Blocks.GetByProtocolId(BlockId);
        var collisionBox = block.GetCollisionShape(world.Reader, world.Entities, x, y, z);
        if (collisionBox is { } box)
        {
            var entitiesInBox = world.Entities.CollectEntitiesOfType<Entity>(box);
            var hasBlockingEntity = entitiesInBox.Any(entity => entity.PreventEntitySpawning);
            if (hasBlockingEntity)
            {
                return false;
            }
        }

        if (!block.CanPlaceAt(new CanPlaceAtContext(world, meta.ToSide(), x, y, z)))
        {
            return false;
        }

        var placementMeta = GetPlacementMetadata(itemStack.GetDamage());
        if (!world.Writer.SetBlockWithoutCallingOnPlaced(x, y, z, BlockId, placementMeta))
        {
            return true;
        }

        if (block.HasBlockEntity
            && world.Entities.GetBlockEntity<BlockEntity>(x, y, z) is IBlockEntityItemData itemData)
        {
            itemData.ApplyItemData(itemStack);
        }

        block.OnPlaced(new OnPlacedEvent(world, entityPlayer, meta.ToSide(), meta.ToSide(), x, y, z));
        world.Broadcaster.PlaySoundAtPos(x + 0.5F, y + 0.5F, z + 0.5F, block.SoundGroup.StepSound, (block.SoundGroup.Volume + 1.0F) / 2.0F, block.SoundGroup.Pitch * 0.8F);
        itemStack.ConsumeItem(entityPlayer);

        return true;
    }

    public override string GetItemNameIs(ItemStack itemStack) => Block.BlockName;

    public override string GetItemName() => Block.BlockName;

    public override string GetDisplayName(ItemStack itemStack)
    {
        var defaultName = base.GetDisplayName(itemStack);
        return _itemData?.GetItemDisplayName(itemStack, defaultName) ?? defaultName;
    }
}
