using BetaSharp.Entities;

namespace BetaSharp.Items.Behaviors;

internal sealed class SaddleBehavior : IItemBehavior
{
    public void UseOnEntity(Item item, ItemStack itemStack, EntityLiving target, EntityPlayer player)
    {
        if (target is EntityPig pig && !pig.Saddled.Value)
        {
            pig.Saddled.Value = true;
            itemStack.ConsumeItem(player);
        }
    }

    public bool PostHit(Item item, ItemStack itemStack, EntityLiving target, EntityPlayer player)
    {
        UseOnEntity(item, itemStack, target, player);
        return true;
    }
}
