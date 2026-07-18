using BetaSharp.Entities;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

internal sealed class FishingRodBehavior : IItemBehavior
{
    public bool IsHandheld(Item item) => true;
    public bool IsHandheldRod(Item item) => true;

    public ItemStack Use(Item item, ItemStack itemStack, IWorldContext world, EntityPlayer player)
    {
        if (player.FishHook != null)
        {
            int durabilityLoss = player.FishHook.catchFish();
            itemStack.DamageItem(durabilityLoss, player);
            player.SwingHand();
        }
        else
        {
            world.Broadcaster.PlaySoundAtEntity(player, "random.bow", 0.5F, 0.4F / (Item.itemRand.NextFloat() * 0.4F + 0.8F));
            if (!world.IsRemote)
            {
                world.SpawnEntity(new EntityFish(world, player));
            }

            player.SwingHand();
        }

        return itemStack;
    }
}
