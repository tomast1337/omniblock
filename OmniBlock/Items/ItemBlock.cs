using OmniBlock.Blocks;
using OmniBlock.Blocks.Entities;
using OmniBlock.Entities;
using OmniBlock.Util.Maths;
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
        => useOnBlock(itemStack, entityPlayer, world, x, y, z, meta, 0.5F);

    public override bool useOnBlock(ItemStack itemStack, EntityPlayer entityPlayer, IWorldContext world, int x, int y, int z, int meta, float hitY)
    {
        if (itemStack.Count == 0) return false;
        var clickedSide = meta.ToSide();
        var block = world.Content.Blocks.GetByProtocolId(BlockId);
        var slab = world.Content.Blocks.Get("slab");
        var isSlab = block.Id == slab.Id;
        var placementMeta = GetPlacementMetadata(itemStack.GetDamage());

        // Merge into the clicked slab before moving to the neighboring cell. Both halves must
        // have the same material; old saves still use the separate double_slab block ID.
        if (isSlab && placementMeta is >= 0 and <= 3 && world.Reader.GetBlockId(x, y, z) == slab.Id)
        {
            var existingMeta = world.Reader.GetBlockMeta(x, y, z);
            var existingUpper = (existingMeta & 8) != 0;
            var oppositeHalf = existingUpper
                ? meta == (int)Side.Down || (meta is >= 2 and <= 5 && hitY <= 0.5F)
                : meta == (int)Side.Up || (meta is >= 2 and <= 5 && hitY > 0.5F);
            if (oppositeHalf && (existingMeta & 7) == placementMeta)
            {
                if (world.Entities.CollectEntitiesOfType<Entity>(new Box(x, y, z, x + 1, y + 1, z + 1))
                    .Any(entity => entity.PreventEntitySpawning)) return false;
                var doubled = world.Content.Blocks.Get("double_slab");
                if (!world.Writer.SetBlockWithoutCallingOnPlaced(x, y, z, doubled.Id, placementMeta)) return false;
                doubled.OnPlaced(new OnPlacedEvent(world, entityPlayer, clickedSide, clickedSide, x, y, z, hitY));
                world.Broadcaster.PlaySoundAtPos(x + 0.5F, y + 0.5F, z + 0.5F, doubled.SoundGroup.StepSound,
                    (doubled.SoundGroup.Volume + 1.0F) / 2.0F, doubled.SoundGroup.Pitch * 0.8F);
                itemStack.ConsumeItem(entityPlayer);
                return true;
            }
        }

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

        var existingBlockId = world.Reader.GetBlockId(x, y, z);
        if (existingBlockId != 0 && !world.Content.Blocks.GetByProtocolId(existingBlockId).Material.IsReplaceable) return false;

        if (y >= ChuckFormat.WorldHeight) return false;

        if (isSlab && placementMeta is >= 0 and <= 3 &&
            (meta == (int)Side.Down || (meta != (int)Side.Up && hitY > 0.5F))) placementMeta |= 8;
        // Existing-world bounds cannot describe an unplaced upper slab. Test the candidate half
        // before mutating the grid, so collision and published metadata agree.
        var collisionBox = isSlab
            ? new Box(x, y + ((placementMeta & 8) != 0 ? 0.5 : 0), z,
                x + 1, y + ((placementMeta & 8) != 0 ? 1 : 0.5), z + 1)
            : block.GetCollisionShape(world.Reader, world.Entities, x, y, z);
        if (collisionBox is { } box)
        {
            var entitiesInBox = world.Entities.CollectEntitiesOfType<Entity>(box);
            var hasBlockingEntity = entitiesInBox.Any(entity => entity.PreventEntitySpawning);
            if (hasBlockingEntity) return false;
        }

        if (!block.CanPlaceAt(new CanPlaceAtContext(world, meta.ToSide(), x, y, z))) return false;

        if (!world.Writer.SetBlockWithoutCallingOnPlaced(x, y, z, BlockId, placementMeta)) return true;

        if (block.HasBlockEntity
            && world.Entities.GetBlockEntity<BlockEntity>(x, y, z) is IBlockEntityItemData itemData)
        {
            itemData.ApplyItemData(itemStack);
        }

        block.OnPlaced(new OnPlacedEvent(world, entityPlayer, clickedSide, clickedSide, x, y, z, hitY));
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
