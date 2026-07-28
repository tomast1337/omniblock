using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

internal sealed class FishingRodBehavior : IItemBehavior
{
    public bool IsHandheld(Item item) => true;
    public bool IsHandheldRod(Item item) => true;

    public ItemStack Use(Item item, ItemStack itemStack, IWorldContext world, EntityPlayer player)
    {
        if (player.FishHook is { } bobber)
        {
            int durabilityLoss = bobber.Behaviors.Find<FishingBobberBehavior>()!.Reel(bobber);
            itemStack.DamageItem(durabilityLoss, player);
            player.SwingHand();
        }
        else
        {
            world.Broadcaster.PlaySoundAtEntity(player, "random.bow", 0.5F, 0.4F / (Item.itemRand.NextFloat() * 0.4F + 0.8F));
            if (!world.IsRemote)
            {
                world.SpawnEntity(FishingBobberBehavior.Cast(world, player));
            }

            player.SwingHand();
        }

        return itemStack;
    }
}
