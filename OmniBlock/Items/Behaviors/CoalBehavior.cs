namespace OmniBlock.Items.Behaviors;

internal sealed class CoalBehavior : IItemBehavior
{
    public string GetItemNameIs(Item item, ItemStack itemStack) => itemStack.GetDamage() == 1 ? "item.charcoal" : "item.coal";
    public IReadOnlyList<string> GetItemAliases(Item item) => ["charcoal:1"];
}
