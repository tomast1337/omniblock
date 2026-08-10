using OmniBlock.Util.Maths;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     Fires an arrow at the target while it is within <paramref name="range" />, then faces it.
///     <para>
///         The projectile is hardcoded to the arrow: it is the only mob-fired projectile with this
///         launch shape, and there is no shared projectile abstraction to parameterize against yet.
///     </para>
/// </summary>
public sealed class RangedAttackBehavior(float range = 10.0F, int cooldownTicks = 30) : IEntityAttackBehavior
{
    public void AttackEntity(EntityCreature self, Entity target, float distance)
    {
        if (!(distance < range))
        {
            return;
        }

        double dx = target.X - self.X;
        double dy = target.Z - self.Z;
        if (self.AttackTime == 0)
        {
            Entity arrow = ArrowBehavior.Shoot(self.World, self);
            double targetHeightOffset = target.Y + target.EyeHeight - 0.2F - arrow.Y;
            float distanceFactor = MathHelper.Sqrt(dx * dx + dy * dy) * 0.2F;
            self.World.Broadcaster.PlaySoundAtEntity(self, "random.bow", 1.0F, 1.0F / (self.Random.NextFloat() * 0.4F + 0.8F));
            self.World.SpawnEntity(arrow);
            arrow.Behaviors.Find<ArrowBehavior>()!.SetHeading(arrow, dx, targetHeightOffset + distanceFactor, dy, 0.6F, 12.0F);
            self.AttackTime = cooldownTicks;
        }

        self.Yaw = (float)(Math.Atan2(dy, dx) * 180.0D / (float)Math.PI) - 90.0F;
        self.HasAttacked = true;
    }
}
