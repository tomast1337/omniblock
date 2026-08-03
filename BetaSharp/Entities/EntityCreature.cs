using BetaSharp.PathFinding;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityCreature(IWorldContext world, EntityType? type = null) : EntityLiving(world, type)
{
    private const float Range = 16.0F;
    private PathEntity? _pathToEntity;
    protected internal bool HasAttacked;
    public Entity? Target { get; set; }

    /// <summary>Base melee damage. Fixed at construction.</summary>
    protected internal int AttackStrength => Definition.AttackStrength;

    /// <summary>Composed attack execution. <c>null</c> means the mob never damages its target.</summary>
    public IEntityAttackBehavior? Attack => Behaviors.Attack;

    /// <summary>
    ///     Composed target acquisition. <c>null</c> means the mob never hunts. Named <c>Targeting</c>
    ///     rather than <c>Target</c> because <see cref="Target" /> already holds the current target.
    /// </summary>
    public IEntityTargetBehavior? Targeting => Behaviors.Targeting;

    protected virtual bool IsMovementCeased => Physics?.IsMovementCeased(this) ?? false;

    protected internal bool HasPath => _pathToEntity != null;

    protected override void TickLiving()
    {
        HasAttacked = IsMovementCeased;
        if (Target == null)
        {
            Target = FindPlayerToAttack();
            if (Target != null)
            {
                World.PathingRequests.RequestPath(this, Target, Range);
            }
        }
        else if (!Target.CanBeTargeted)
        {
            Target = null;
        }
        else
        {
            float distance = Target.GetDistance(this);
            if (CanSee(Target))
            {
                attackEntity(Target, distance);
            }
            else
            {
                attackBlockedEntity(Target, distance);
            }
        }

        if (HasAttacked || Target == null || (_pathToEntity != null && Random.NextInt(20) != 0))
        {
            if (!HasAttacked && ((_pathToEntity == null && Random.NextInt(80) == 0) || Random.NextInt(80) == 0))
            {
                FindRandomWanderTarget();
            }
        }
        else
        {
            World.PathingRequests.RequestPath(this, Target, Range);
        }

        int floorY = MathHelper.Floor(BoundingBox.MinY + 0.5D);
        bool isInWater = InWater;
        bool isTouchingLava = IsTouchingLava;
        Pitch = 0.0F;
        if (_pathToEntity != null && Random.NextInt(100) != 0)
        {
            Vec3D? pos = _pathToEntity.GetPosition(this);
            double distance = Width * 2.0F;

            while (pos != null && pos.Value.squareDistanceTo(new Vec3D(X, pos.Value.y, Z)) < distance * distance)
            {
                _pathToEntity?.IncrementPathIndex();
                if (_pathToEntity is { IsFinished: true })
                {
                    pos = null;
                    _pathToEntity = null;
                }
                else
                {
                    pos = _pathToEntity?.GetPosition(this);
                }
            }

            Jumping = false;
            if (pos != null)
            {
                double dx = pos.Value.x - X;
                double dz = pos.Value.z - Z;
                double verticalOffset = pos.Value.y - floorY;
                float targetYaw = (float)(Math.Atan2(dz, dx) * 180.0D / (float)Math.PI) - 90.0F;
                float yawDelta = targetYaw - Yaw;

                for (ForwardSpeed = MovementSpeed; yawDelta < -180.0F; yawDelta += 360.0F)
                {
                }

                while (yawDelta >= 180.0F)
                {
                    yawDelta -= 360.0F;
                }

                if (yawDelta > 30.0F)
                {
                    yawDelta = 30.0F;
                }

                if (yawDelta < -30.0F)
                {
                    yawDelta = -30.0F;
                }

                Yaw += yawDelta;
                if (HasAttacked && Target != null)
                {
                    double targetDeltaX = Target.X - X;
                    double targetDeltaZ = Target.Z - Z;
                    float previousYaw = Yaw;
                    Yaw = (float)(Math.Atan2(targetDeltaZ, targetDeltaX) * 180.0D / (float)Math.PI) - 90.0F;
                    yawDelta = (previousYaw - Yaw + 90.0F) * (float)Math.PI / 180.0F;
                    SidewaysSpeed = -MathHelper.Sin(yawDelta) * ForwardSpeed * 1.0F;
                    ForwardSpeed = MathHelper.Cos(yawDelta) * ForwardSpeed * 1.0F;
                }

                if (verticalOffset > 0.0D)
                {
                    Jumping = true;
                }
            }

            if (Target != null)
            {
                faceEntity(Target, 30.0F, 30.0F);
            }

            if (HorizontalCollision && !HasPath)
            {
                Jumping = true;
            }

            if (Random.NextFloat() < 0.8F && (isInWater || isTouchingLava))
            {
                Jumping = true;
            }
        }
        else
        {
            base.TickLiving();
            _pathToEntity = null;
        }
    }

    private void FindRandomWanderTarget()
    {
        bool foundWanderTarget = false;
        BlockPos bestTile = new(-1, -1, -1);
        float bestCost = float.MinValue;

        for (int _ = 0; _ < 10; ++_)
        {
            BlockPos tile = new(
                MathHelper.Floor(X + Random.NextInt(13) - 6.0D),
                MathHelper.Floor(Y + Random.NextInt(7) - 3.0D),
                MathHelper.Floor(Z + Random.NextInt(13) - 6.0D)
            );
            float cost = GetBlockPathWeight(tile.X, tile.Y, tile.Z);
            if (cost <= bestCost)
            {
                continue;
            }

            bestCost = cost;
            bestTile = tile;
            foundWanderTarget = true;
        }

        if (foundWanderTarget)
        {
            World.PathingRequests.RequestPath(this, bestTile.X, bestTile.Y, bestTile.Z, 10.0F);
        }
    }

    protected virtual void attackEntity(Entity entity, float distance) => Attack?.AttackEntity(this, entity, distance);

    protected virtual void attackBlockedEntity(Entity entity, float distance) => Attack?.AttackBlockedEntity(this, entity, distance);

    /// <summary>
    ///     How attractive a block is to path towards. Declared by the Physics slot; without one
    ///     every block is equally uninteresting.
    /// </summary>
    protected virtual float GetBlockPathWeight(int x, int y, int z) => Physics?.GetBlockPathWeight(this, x, y, z) ?? 0.0F;

    protected virtual Entity? FindPlayerToAttack() => Targeting?.FindPlayerToAttack(this);

    protected override bool CanSpawnHere()
    {
        BlockPos tile = new(MathHelper.Floor(X), MathHelper.Floor(BoundingBox.MinY), MathHelper.Floor(Z));
        return base.CanSpawnHere() && GetBlockPathWeight(tile.X, tile.Y, tile.Z) >= 0.0F;
    }

    internal void setPathToEntity(PathEntity? pathToEntity) => _pathToEntity = pathToEntity;
}
