using BetaSharp.Util.Maths;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     What a tameable mob does with the tick its pathfinding did not use: a tamed one keeps up with
///     its owner and sits down when it cannot find them, an untamed one goes looking for prey. One
///     behavior, because a mob is either tamed or not and never does both in a tick.
/// </summary>
public sealed class FollowOwnerBehavior : IEntityTicker
{
    private readonly float _followRange;
    private readonly float _leashRange;
    private readonly int _preyChanceOneIn;
    private readonly double _preyRadius;
    private readonly float _teleportRange;

    public FollowOwnerBehavior(in EntityBehaviorContext context)
    {
        _followRange = context.Float("follow_range", 5.0F);
        _leashRange = context.Float("leash_range", 16.0F);
        _teleportRange = context.Float("teleport_range", 12.0F);
        _preyRadius = context.Double("prey_radius", 16.0D);
        _preyChanceOneIn = context.Int("prey_chance_one_in", 100);
    }

    public void AfterTickLiving(EntityLiving self)
    {
        if (self is not EntityCreature mob)
        {
            return;
        }

        if (mob.Behaviors.Find<TameableBehavior>() is not { } tame)
        {
            return;
        }

        if (!mob.HasAttacked && !mob.HasPath && tame.IsTamed(mob) && mob.Vehicle == null)
        {
            KeepUpWithOwner(mob, tame);
        }
        else if (mob.Target == null && !mob.HasPath && !tame.IsTamed(mob) && mob.World.Random.NextInt(_preyChanceOneIn) == 0)
        {
            HuntPrey(mob);
        }
    }

    private void KeepUpWithOwner(EntityCreature self, TameableBehavior tame)
    {
        EntityPlayer? owner = self.World.Entities.Players.Find(player => player.Name != null && player.Name.Equals(tame.Owner(self), StringComparison.OrdinalIgnoreCase));
        if (owner == null)
        {
            // Nobody to follow, so it waits where it is, unless that would be in water.
            if (!self.IsInWater)
            {
                tame.SetSitting(self, true);
            }

            return;
        }

        float distance = owner.GetDistance(self);
        if (distance > _followRange)
        {
            PathOrTeleport(self, owner, distance);
        }
    }

    /// <summary>
    ///     Queues a path to the owner, and teleports beside them when it has no path already and is
    ///     far enough away not to be seen doing it. The queued path lands on a later tick, via
    ///     PathingCoordinator.RunBatch.
    /// </summary>
    private void PathOrTeleport(EntityCreature self, Entity owner, float distance)
    {
        self.World.PathingRequests.RequestPath(self, owner, _leashRange);
        if (self.HasPath || distance <= _teleportRange)
        {
            return;
        }

        int cornerX = MathHelper.Floor(owner.X) - 2;
        int cornerZ = MathHelper.Floor(owner.Z) - 2;
        int floorY = MathHelper.Floor(owner.BoundingBox.MinY);

        for (int dx = 0; dx <= 4; ++dx)
        {
            for (int dz = 0; dz <= 4; ++dz)
            {
                // Skip the middle, so it never lands on top of the owner.
                if (dx >= 1 && dz >= 1 && dx <= 3 && dz <= 3)
                {
                    continue;
                }

                if (!self.World.Reader.ShouldSuffocate(cornerX + dx, floorY - 1, cornerZ + dz))
                {
                    continue;
                }

                if (self.World.Reader.ShouldSuffocate(cornerX + dx, floorY, cornerZ + dz))
                {
                    continue;
                }

                if (self.World.Reader.ShouldSuffocate(cornerX + dx, floorY + 1, cornerZ + dz))
                {
                    continue;
                }

                self.SetPositionAndAnglesKeepPrevAngles(cornerX + dx + 0.5F, floorY, cornerZ + dz + 0.5F, self.Yaw, self.Pitch);
                return;
            }
        }
    }

    /// <summary>
    ///     Prey is anything with a fleece, since a sheep has no class of its own to collect by.
    /// </summary>
    private void HuntPrey(EntityCreature self)
    {
        List<EntityLiving> prey = self.World.Entities
            .CollectEntitiesOfType<EntityLiving>(new Box(self.X, self.Y, self.Z, self.X + 1.0D, self.Y + 1.0D, self.Z + 1.0D).Expand(_preyRadius, 4.0D, _preyRadius))
            .FindAll(candidate => candidate.Behaviors.Find<WoolBehavior>() != null);

        if (prey.Count > 0)
        {
            self.Target = prey[self.World.Random.NextInt(prey.Count)];
        }
    }
}
