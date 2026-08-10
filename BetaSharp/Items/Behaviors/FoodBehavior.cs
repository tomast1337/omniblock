using OmniBlock.Entities;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items.Behaviors;

internal sealed class FoodBehavior : IItemBehavior
{
    private readonly Item? _returnItem;

    internal FoodBehavior(int healAmount, bool isMeat, Item? returnItem = null)
    {
        HealAmount = healAmount;
        IsMeat = isMeat;
        _returnItem = returnItem;
    }

    internal int HealAmount { get; }

    internal bool IsMeat { get; }

    public ItemStack Use(Item item, ItemStack itemStack, IWorldContext world, EntityPlayer player)
    {
        itemStack.ConsumeItem(player);
        player.Heal(HealAmount);
        return _returnItem != null ? new ItemStack(_returnItem) : itemStack;
    }
}
