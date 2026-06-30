using BetaSharp.Entities;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

internal sealed class ThrowableBehavior : IItemBehavior
{
    private readonly Func<IWorldContext, EntityPlayer, Entity> _createProjectile;
    private readonly int _maxCount;

    internal ThrowableBehavior(int maxCount, Func<IWorldContext, EntityPlayer, Entity> createProjectile)
    {
        _maxCount = maxCount;
        _createProjectile = createProjectile;
    }

    public void Apply(Item item) => item.maxCount = _maxCount;

    public ItemStack Use(Item item, ItemStack itemStack, IWorldContext world, EntityPlayer player)
    {
        itemStack.ConsumeItem(player);
        world.Broadcaster.PlaySoundAtEntity(player, "random.bow", 0.5F, 0.4F / (Item.itemRand.NextFloat() * 0.4F + 0.8F));
        if (!world.IsRemote)
        {
            world.SpawnEntity(_createProjectile(world, player));
        }

        return itemStack;
    }
}
