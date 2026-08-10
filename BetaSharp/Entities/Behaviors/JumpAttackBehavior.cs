using OmniBlock.Util.Maths;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     Lunges at the target when it sits in the <paramref name="minRange" />..<paramref name="maxRange" />
///     band, on a 1-in-<paramref name="chanceOneIn" /> roll. Any other tick delegates to
///     <paramref name="fallback" />.
/// </summary>
public sealed class JumpAttackBehavior(float minRange, float maxRange, int chanceOneIn, IEntityAttackBehavior? fallback = null) : IEntityAttackBehavior, IEntityBehaviorGroup
{
    /// <summary>What runs on the ticks the lunge does not, exposed so the nesting is inspectable.</summary>
    public IEntityAttackBehavior? Fallback => fallback;

    public void AttackEntity(EntityCreature self, Entity target, float distance)
    {
        if (distance > minRange && distance < maxRange && self.Random.NextInt(chanceOneIn) == 0)
        {
            if (!self.OnGround)
            {
                return;
            }

            double dx = target.X - self.X;
            double dz = target.Z - self.Z;
            float horizontalDistance = MathHelper.Sqrt(dx * dx + dz * dz);
            self.VelocityX = dx / horizontalDistance * 0.5D * 0.8F + self.VelocityX * 0.2F;
            self.VelocityZ = dz / horizontalDistance * 0.5D * 0.8F + self.VelocityZ * 0.2F;
            self.VelocityY = 0.4F;
            return;
        }

        fallback?.AttackEntity(self, target, distance);
    }

    public IEnumerable<object> Children => fallback is null ? [] : [fallback];
}
