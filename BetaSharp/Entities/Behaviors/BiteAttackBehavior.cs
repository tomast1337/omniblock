namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Bites whatever it is on top of, hitting harder once tamed — a wolf fighting for someone is
///     worth twice one fighting for itself.
///     <para>
///         Unlike <see cref="MeleeAttackBehavior" /> this has no cooldown of its own: it sets the
///         swing timer for the animation but never waits on it, which is why a cornered wolf is so
///         much more dangerous than its damage number suggests.
///     </para>
/// </summary>
public sealed class BiteAttackBehavior(float range, int damage, int tamedDamage) : IEntityAttackBehavior
{
    public void AttackEntity(EntityCreature self, Entity target, float distance)
    {
        if (!(distance < range)) return;
        if (!(target.BoundingBox.MaxY > self.BoundingBox.MinY) || !(target.BoundingBox.MinY < self.BoundingBox.MaxY)) return;

        self.AttackTime = 20;

        bool tamed = self.Behaviors.Find<TameableBehavior>()?.IsTamed(self) == true;
        target.Damage(self, tamed ? tamedDamage : damage);
    }
}
