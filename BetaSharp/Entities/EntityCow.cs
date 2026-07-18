using BetaSharp.Items;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityCow : EntityAnimal
{
    private static readonly Item s_bucket = Item.ByName("bucket");
    private static readonly Item s_milk = Item.ByName("milk");
    private static readonly Item s_leather = Item.ByName("leather");
    public EntityCow(IWorldContext world) : base(world)
    {
        Texture = "/mob/cow.png";
        SetBoundingBoxSpacing(0.9F, 1.3F);
    }

    public override EntityType Type => EntityRegistry.Cow;

    protected override string? LivingSound => "mob.cow";

    protected override string? HurtSound => "mob.cowhurt";

    protected override string? DeathSound => "mob.cowhurt";

    protected override float SoundVolume => 0.4F;

    protected override int DropItem => s_leather.Id;

    public override bool Interact(EntityPlayer player)
    {
        ItemStack? heldBucket = player.Inventory.ItemInHand;
        if (heldBucket == null || heldBucket.ItemId != s_bucket.Id) return false;
        player.Inventory.SetStack(player.Inventory.SelectedSlot, new ItemStack(s_milk));
        return true;
    }
}
