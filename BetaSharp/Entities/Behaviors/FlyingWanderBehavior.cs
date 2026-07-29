using BetaSharp.Entities.State;
using BetaSharp.Util.Maths;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     The ghast's idle flight: drifts towards a waypoint picked at random, re-picking it once
///     reached or once it has drifted out of reach, and abandoning it if the way there is blocked.
///     No path, no ground, just a heading nudged every few ticks.
///     <para>
///         Answers <c>true</c> from the AI tick, so the mob does none of the default idle work: no
///         ageing towards despawn, no glancing at passers-by. It runs the despawn check itself.
///     </para>
/// </summary>
public sealed class FlyingWanderBehavior : IEntityTicker
{
    private readonly double _acceleration;
    private readonly StateHandle<int> _courseChangeCooldown;
    private readonly double _maxDistance;
    private readonly float _range;
    private readonly int _recheckTicks;
    private readonly StateHandle<double> _waypointX;
    private readonly StateHandle<double> _waypointY;
    private readonly StateHandle<double> _waypointZ;

    public FlyingWanderBehavior(in EntityBehaviorContext context)
    {
        _range = context.Float("range", 16.0F);
        _maxDistance = context.Double("max_distance", 60.0D);
        _acceleration = context.Double("acceleration", 0.1D);
        _recheckTicks = context.Int("recheck_ticks", 5);
        _waypointX = context.DeclareDouble();
        _waypointY = context.DeclareDouble();
        _waypointZ = context.DeclareDouble();
        _courseChangeCooldown = context.DeclareInt();
    }

    public bool OnTickLiving(EntityLiving self)
    {
        self.TickDespawn();

        EntityState state = self.State;
        double dx = state[_waypointX] - self.X;
        double dy = state[_waypointY] - self.Y;
        double dz = state[_waypointZ] - self.Z;
        double distance = MathHelper.Sqrt(dx * dx + dy * dy + dz * dz);

        if (distance < 1.0D || distance > _maxDistance)
        {
            state[_waypointX] = self.X + (self.Random.NextFloat() * 2.0F - 1.0F) * _range;
            state[_waypointY] = self.Y + (self.Random.NextFloat() * 2.0F - 1.0F) * _range;
            state[_waypointZ] = self.Z + (self.Random.NextFloat() * 2.0F - 1.0F) * _range;
        }

        if (state[_courseChangeCooldown]-- > 0)
        {
            return true;
        }

        state[_courseChangeCooldown] += self.Random.NextInt(_recheckTicks) + 2;
        if (IsCourseTraversable(self, distance))
        {
            self.VelocityX += dx / distance * _acceleration;
            self.VelocityY += dy / distance * _acceleration;
            self.VelocityZ += dz / distance * _acceleration;
        }
        else
        {
            state[_waypointX] = self.X;
            state[_waypointY] = self.Y;
            state[_waypointZ] = self.Z;
        }

        return true;
    }

    /// <summary>
    ///     Steps the mob's own box along the heading, one box-length at a time, and gives up at the
    ///     first thing it would run into.
    /// </summary>
    private bool IsCourseTraversable(EntityLiving self, double distance)
    {
        EntityState state = self.State;
        double stepX = (state[_waypointX] - self.X) / distance;
        double stepY = (state[_waypointY] - self.Y) / distance;
        double stepZ = (state[_waypointZ] - self.Z) / distance;
        Box box = self.BoundingBox;

        for (int step = 1; step < distance; ++step)
        {
            box.Translate(stepX, stepY, stepZ);
            if (self.World.Entities.GetEntityCollisionsScratch(self, box).Count > 0)
            {
                return false;
            }
        }

        return true;
    }
}
