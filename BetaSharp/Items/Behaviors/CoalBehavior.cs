namespace BetaSharp.Items.Behaviors;

internal sealed class CoalBehavior : IItemBehavior
{
    public void Apply(Item item)
    {
        item.setHasSubtypes(true);
        item.setMaxDamage(0);
    }

    public string GetItemNameIS(Item item, ItemStack itemStack)
        => itemStack.getDamage() == 1 ? "item.charcoal" : "item.coal";

    public IReadOnlyList<string> GetItemAliases(Item item) => ["charcoal:1"];
}
