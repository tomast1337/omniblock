using BetaSharp.Items;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Exchanges the player's held item for another on right-click, as milking a cow swaps a bucket
///     for milk. Stateless and fully parameterised, so any "give X, get Y" interaction is data.
/// </summary>
public sealed class SwapHeldItemBehavior(Item required, Item result) : IEntityInteractable
{
    public bool OnInteract(Entity self, EntityPlayer player)
    {
        ItemStack? held = player.Inventory.ItemInHand;
        if (held == null || held.ItemId != required.Id)
        {
            return false;
        }

        player.Inventory.SetStack(player.Inventory.SelectedSlot, new ItemStack(result));
        return true;
    }
}
