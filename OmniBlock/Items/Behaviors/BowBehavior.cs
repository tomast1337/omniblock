using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items.Behaviors;

internal sealed class BowBehavior : IItemBehavior
{
    private static readonly Item s_arrow = Item.ByName("arrow");

    public ItemStack Use(Item item, ItemStack itemStack, IWorldContext world, EntityPlayer player)
    {
        if (player.Inventory.ConsumeInventoryItem(s_arrow.Id))
        {
            world.Broadcaster.PlaySoundAtEntity(player, "random.bow", 1.0F, 1.0F / (Item.s_itemRand.NextFloat() * 0.4F + 0.8F));
            if (!world.IsRemote)
            {
                world.SpawnEntity(ArrowBehavior.Shoot(world, player));
            }
        }

        return itemStack;
    }
}
