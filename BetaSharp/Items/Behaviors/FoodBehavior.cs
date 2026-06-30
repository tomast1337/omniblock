using BetaSharp.Entities;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

internal sealed class FoodBehavior : IItemBehavior
{
    private readonly int _maxCount;
    private readonly Item? _returnItem;

    internal FoodBehavior(int healAmount, bool isWolfsFavoriteMeat, int maxCount = 1, Item? returnItem = null)
    {
        HealAmount = healAmount;
        IsWolfsFavoriteMeat = isWolfsFavoriteMeat;
        _maxCount = maxCount;
        _returnItem = returnItem;
    }

    internal int HealAmount { get; }

    internal bool IsWolfsFavoriteMeat { get; }

    public void Apply(Item item) => item.maxCount = _maxCount;

    public ItemStack Use(Item item, ItemStack itemStack, IWorldContext world, EntityPlayer player)
    {
        itemStack.ConsumeItem(player);
        player.Heal(HealAmount);
        return _returnItem != null ? new ItemStack(_returnItem) : itemStack;
    }
}
