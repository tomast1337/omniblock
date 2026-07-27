using BetaSharp.Items;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityCow : EntityAnimal
{
    private static readonly Item s_bucket = Item.ByName("bucket");
    private static readonly Item s_milk = Item.ByName("milk");
    public EntityCow(IWorldContext world) : base(world, EntityRegistry.ByName("cow").RequireDefinition())
    {
    }

    public override bool Interact(EntityPlayer player)
    {
        ItemStack? heldBucket = player.Inventory.ItemInHand;
        if (heldBucket == null || heldBucket.ItemId != s_bucket.Id) return false;
        player.Inventory.SetStack(player.Inventory.SelectedSlot, new ItemStack(s_milk));
        return true;
    }
}
