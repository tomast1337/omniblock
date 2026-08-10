using OmniBlock.Entities;

namespace OmniBlock.Items.Behaviors;

internal sealed class SaddleBehavior : IItemBehavior
{
    public void UseOnEntity(Item item, ItemStack itemStack, EntityLiving target, EntityPlayer player)
    {
        // Anything declaring a "saddled" property can be saddled, rather than the pig specifically.
        if (target.Synced<bool>("saddled") is { Value: false } saddled)
        {
            saddled.Value = true;
            itemStack.ConsumeItem(player);
        }
    }

    public bool PostHit(Item item, ItemStack itemStack, EntityLiving target, EntityPlayer player)
    {
        UseOnEntity(item, itemStack, target, player);
        return true;
    }
}
