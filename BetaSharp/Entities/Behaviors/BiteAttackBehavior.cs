namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     Bites whatever it is on top of, hitting harder once tamed.
///     <para>
///         Unlike <see cref="MeleeAttackBehavior" /> this has no cooldown of its own: it sets the
///         swing timer for the animation but never waits on it, so it bites every tick it is in range.
///     </para>
/// </summary>
public sealed class BiteAttackBehavior(float range, int damage, int tamedDamage) : IEntityAttackBehavior
{
    public void AttackEntity(EntityCreature self, Entity target, float distance)
    {
        if (!(distance < range))
        {
            return;
        }

        if (!(target.BoundingBox.MaxY > self.BoundingBox.MinY) || !(target.BoundingBox.MinY < self.BoundingBox.MaxY))
        {
            return;
        }

        self.AttackTime = 20;

        bool tamed = self.Behaviors.Find<TameableBehavior>()?.IsTamed(self) == true;
        target.Damage(self, tamed ? tamedDamage : damage);
    }
}
