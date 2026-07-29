namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Wraps another attack behavior with a chance to lose interest in bright light, which makes a
///     spider harmless by day without changing how it attacks by night. A decorator, so it composes
///     over whatever the mob's real attack is.
/// </summary>
public sealed class LoseTargetInDaylightBehavior(IEntityAttackBehavior inner, float brightnessThreshold, int chanceOneIn)
    : IEntityAttackBehavior
{
    /// <summary>The attack this decorates, exposed so the composition is inspectable.</summary>
    public IEntityAttackBehavior Inner => inner;

    public void AttackEntity(EntityCreature self, Entity target, float distance)
    {
        if (self.GetBrightnessAtEyes(1.0F) > brightnessThreshold && self.Random.NextInt(chanceOneIn) == 0)
        {
            self.Target = null;
            return;
        }

        inner.AttackEntity(self, target, distance);
    }

    public void AttackBlockedEntity(EntityCreature self, Entity target, float distance) =>
        inner.AttackBlockedEntity(self, target, distance);
}
