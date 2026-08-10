using OmniBlock.Entities.State;
using OmniBlock.Items;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     Drops an egg on a randomised timer. The countdown is per-chicken, so it lives in
///     <see cref="EntityState" /> behind a handle resolved once at load.
/// </summary>
public sealed class LayEggsBehavior : IEntityTicker
{
    private static readonly Item s_egg = Item.ByName("egg");
    private readonly int _minDelay;
    private readonly int _range;

    private readonly StateHandle<int> _ticksUntilNextEgg;

    public LayEggsBehavior(in EntityBehaviorContext context)
    {
        _minDelay = context.Int("min_delay", 6000);
        _range = context.Int("delay_range", 6000);
        _ticksUntilNextEgg = context.DeclareInt();
    }

    public void OnTickMovement(EntityLiving self)
    {
        if (self.World.IsRemote)
        {
            return;
        }

        int remaining = self.State[_ticksUntilNextEgg];
        if (remaining <= 0)
        {
            // Zero means "never seeded" for an entity built before Reset ran, as well as "due now".
            Reset(self);
            return;
        }

        self.State[_ticksUntilNextEgg] = --remaining;
        if (remaining > 0)
        {
            return;
        }

        self.World.Broadcaster.PlaySoundAtEntity(self, "mob.chickenplop", 1.0F, (self.Random.NextFloat() - self.Random.NextFloat()) * 0.2F + 1.0F);
        self.DropItem(s_egg.Id, 1);
        Reset(self);
    }

    /// <summary>Seeds the first countdown; a freshly created state slot starts at zero.</summary>
    public void Reset(Entity self) => self.State[_ticksUntilNextEgg] = self.Random.NextInt(_range) + _minDelay;
}
