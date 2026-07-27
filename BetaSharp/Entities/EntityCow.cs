using BetaSharp.Entities.Behaviors;
using BetaSharp.Items;
using BetaSharp.Loot;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityCow : EntityAnimal
{
    private static readonly Item s_bucket = Item.ByName("bucket");
    private static readonly Item s_milk = Item.ByName("milk");
    private static readonly Item s_leather = Item.ByName("leather");
    public EntityCow(IWorldContext world) : base(world, EntityRegistry.Cow.RequireDefinition())
    {
        Loot = new LootTableBehavior(LootTable.Single(s_leather, 0, 2));
    }

    public override EntityType Type => EntityRegistry.Cow;

    public override bool Interact(EntityPlayer player)
    {
        ItemStack? heldBucket = player.Inventory.ItemInHand;
        if (heldBucket == null || heldBucket.ItemId != s_bucket.Id) return false;
        player.Inventory.SetStack(player.Inventory.SelectedSlot, new ItemStack(s_milk));
        return true;
    }
}
