namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     Damages the target once every 20 ticks while it is within <paramref name="range" /> and
///     vertically overlapping. Damage is read from <see cref="EntityCreature.AttackStrength" /> at
///     call time, so a mob that raises it later still hits harder.
/// </summary>
public sealed class MeleeAttackBehavior(float range = 2.0F) : IEntityAttackBehavior
{
    public void AttackEntity(EntityCreature self, Entity target, float distance)
    {
        if (self.AttackTime > 0 || !(distance < range) || !(target.BoundingBox.MaxY > self.BoundingBox.MinY) || !(target.BoundingBox.MinY < self.BoundingBox.MaxY))
        {
            return;
        }

        self.AttackTime = 20;
        target.Damage(self, self.AttackStrength);
    }
}
