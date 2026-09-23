using OmniBlock.Entities;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items.Behaviors;

internal sealed class FlintAndSteelBehavior : IItemBehavior
{
    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
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

        var blockId = world.Reader.GetBlockId(x, y, z);
        if (blockId == 0)
        {
            world.Broadcaster.PlaySoundAtPos(x + 0.5D, y + 0.5D, z + 0.5D, "fire.ignite", 1.0F, Item.s_itemRand.NextFloat() * 0.4F + 0.8F);
            world.Writer.SetBlock(x, y, z, world.Content.Blocks.Get("omniblock:fire").Id);
        }

        itemStack.DamageItem(1, player);
        return true;
    }
}
