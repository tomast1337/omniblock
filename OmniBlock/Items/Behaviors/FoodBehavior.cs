using OmniBlock.Entities;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items.Behaviors;

internal sealed class FoodBehavior : IItemBehavior
{
    internal FoodBehavior(int healAmount, bool isMeat, Item? returnItem = null)
    {
        HealAmount = healAmount;
        IsMeat = isMeat;
        ReturnItem = returnItem;
    }

    internal int HealAmount { get; }

    internal bool IsMeat { get; }
    internal Item? ReturnItem { get; }

    public ItemStack Use(Item item, ItemStack itemStack, IWorldContext world, EntityPlayer player)
    {
        itemStack.ConsumeItem(player);
        player.Heal(HealAmount);
        return ReturnItem != null ? new ItemStack(ReturnItem) : itemStack;
    }
}
