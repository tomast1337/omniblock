using OmniBlock.Blocks;
using OmniBlock.Entities;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items;

internal class ItemBlock : Item
{
    private readonly int _blockId;

    public ItemBlock(int id) : base(id)
    {
        _blockId = id + 256;
        SetTextureId(Block.Blocks[id + 256].GetTexture(2.ToSide()));
    }

    public override bool useOnBlock(ItemStack itemStack, EntityPlayer entityPlayer, IWorldContext world, int x, int y, int z, int meta)
    {
        if (world.Reader.GetBlockId(x, y, z) == BlockRegistry.Get("snow").Id)
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

        int existingBlockId = world.Reader.GetBlockId(x, y, z);
        if (existingBlockId != 0 && !Block.Blocks[existingBlockId].Material.IsReplaceable)
        {
            return false;
        }

        if (y >= ChuckFormat.WorldHeight)
        {
            return false;
        }

        Block block = Block.Blocks[_blockId];
        Box? collisionBox = block.GetCollisionShape(world.Reader, world.Entities, x, y, z);
        if (collisionBox is { } box)
        {
            List<Entity> entitiesInBox = world.Entities.CollectEntitiesOfType<Entity>(box);
            bool hasBlockingEntity = entitiesInBox.Any(entity => entity.PreventEntitySpawning);
            if (hasBlockingEntity)
            {
                return false;
            }
        }

        if (!block.CanPlaceAt(new CanPlaceAtContext(world, meta.ToSide(), x, y, z)))
        {
            return false;
        }

        int placementMeta = GetPlacementMetadata(itemStack.GetDamage());
        if (!world.Writer.SetBlockWithoutCallingOnPlaced(x, y, z, _blockId, placementMeta))
        {
            return true;
        }

        Block.Blocks[_blockId].OnPlaced(new OnPlacedEvent(world, entityPlayer, meta.ToSide(), meta.ToSide(), x, y, z));
        world.Broadcaster.PlaySoundAtPos(x + 0.5F, y + 0.5F, z + 0.5F, block.SoundGroup.StepSound, (block.SoundGroup.Volume + 1.0F) / 2.0F, block.SoundGroup.Pitch * 0.8F);
        itemStack.ConsumeItem(entityPlayer);

        return true;

    }

    public override string GetItemNameIs(ItemStack itemStack) => Block.Blocks[_blockId].BlockName;

    public override string GetItemName() => Block.Blocks[_blockId].BlockName;
}
