using BetaSharp.Entities;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

internal sealed class BowBehavior : IItemBehavior
{
    public void Apply(Item item) => item.maxCount = 1;

    public ItemStack Use(Item item, ItemStack itemStack, IWorldContext world, EntityPlayer player)
    {
        if (player.Inventory.ConsumeInventoryItem(Item.ByName("arrow").id))
        {
            world.Broadcaster.PlaySoundAtEntity(player, "random.bow", 1.0F, 1.0F / (Item.itemRand.NextFloat() * 0.4F + 0.8F));
            if (!world.IsRemote)
            {
                world.SpawnEntity(new EntityArrow(world, player));
            }
        }

        return itemStack;
    }
}
