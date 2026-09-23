using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items.Behaviors;

internal sealed class FishingRodBehavior(int cast) : IItemBehavior
{
    /// <summary>The icon for a rod with a bobber in the water.</summary>
    public int CastTextureId => cast;

    public bool IsHandheld(Item item) => true;
    public bool IsHandheldRod(Item item) => true;

    public ItemStack Use(Item item, ItemStack itemStack, IWorldContext world, EntityPlayer player)
    {
        if (player.FishHook is { } bobber)
        {
            var durabilityLoss = bobber.Behaviors.Find<FishingBobberBehavior>()!.Reel(bobber);
            itemStack.DamageItem(durabilityLoss, player);
        }
        else
        {
            world.Broadcaster.PlaySoundAtEntity(player, "random.bow", 0.5F, 0.4F / (Item.s_itemRand.NextFloat() * 0.4F + 0.8F));
            if (!world.IsRemote) world.SpawnEntity(FishingBobberBehavior.Cast(world, player));
        }

        player.SwingHand();

        return itemStack;
    }
}
