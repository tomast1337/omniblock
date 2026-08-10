using OmniBlock.NBT;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Entities;

/// <summary>
///     The shared body for non-living entities that are nothing but their definition and behaviors:
///     what <see cref="EntityLiving" /> is to mobs, this is to objects like primed TNT. Applies the
///     definition's box and flags and owns no state of its own; anything mutable lives in
///     <see cref="Entity.State" /> and persists through composed <see cref="IEntityPersistence" />.
/// </summary>
public class EntityObject : Entity
{
    public EntityObject(IWorldContext world, EntityType type) : base(world, type)
    {
        Definition = type.RequireDefinition();
        PreventEntitySpawning = Definition.PreventEntitySpawning;
        RenderDistanceWeight = Definition.RenderDistanceWeight;
        IgnoreFrustumCheck = Definition.IgnoreFrustumCheck;
        SetBoundingBoxSpacing(Definition.Width, Definition.Height);
        StandingEyeHeight = Height * Definition.EyeHeightScale;
    }

    protected EntityDefinition Definition { get; }

    public override bool HasCollision => Definition.Collidable && !Dead;

    public override float TargetingMargin => Definition.TargetingMargin;

    public override bool IsPushable => Definition.Pushable;

    protected override double PassengerRidingHeight => Height * Definition.PassengerRideHeightScale + Definition.PassengerRideOffset;

    /// <summary>A solid one is a hull others collide with; the rest are walked through.</summary>
    public override Box? GetBoundingBox() => Definition.SolidCollisionShape ? BoundingBox : null;

    public override Box? GetCollisionAgainstShape(Entity entity) =>
        Definition.SolidCollisionShape ? entity.BoundingBox : null;

    public override void UpdatePassengerPosition()
    {
        if (Physics?.OnUpdatePassengerPosition(this) != true)
        {
            base.UpdatePassengerPosition();
        }
    }

    public override void AnimateHurt()
    {
        if (Behaviors.Lifecycle?.OnAnimateHurt(this) != true)
        {
            base.AnimateHurt();
        }
    }

    public override void OnCollision(Entity entity)
    {
        if (Physics?.OnCollision(this, entity) != true)
        {
            base.OnCollision(entity);
        }
    }

    public override void MarkDead()
    {
        Behaviors.Lifecycle?.OnRemoved(this);
        base.MarkDead();
    }

    /// <summary>
    ///     A behavior may take the synced position for itself; a bobber eases towards it. Failing
    ///     that, an arrow declares it stays put, so it never climbs out of what it is stuck in.
    /// </summary>
    public override void SetPositionAndAnglesAvoidEntities(double x, double y, double z, float yaw, float pitch, int steps)
    {
        if (Physics?.OnPositionSync(this, x, y, z, yaw, pitch, steps) == true)
        {
            return;
        }

        if (Definition.PositionSyncAvoidsEntities)
        {
            base.SetPositionAndAnglesAvoidEntities(x, y, z, yaw, pitch, steps);
            return;
        }

        SetPosition(x, y, z);
        SetRotation(yaw, pitch);
    }

    /// <summary>Composed: a dropped item spends hit points, primed TNT ignores the hit.</summary>
    public override bool Damage(Entity? entity, int amount) =>
        Behaviors.Lifecycle?.Damage(this, entity, amount) ?? base.Damage(entity, amount);

    protected override void Damage(int amt)
    {
        if (Behaviors.Lifecycle?.Damage(this, null, amt) is null)
        {
            base.Damage(amt);
        }
    }

    protected override bool BypassesSteppingEffects() => Definition.MakesStepSounds;

    /// <summary>Zero for every non-living entity; the vertical shadow offset is a mob thing.</summary>
    public override float GetShadowRadius() => 0.0F;

    public override bool ShouldRender(Vec3D vec) => Physics?.ShouldRender(this) ?? base.ShouldRender(vec);

    public override void SetVelocityClient(double vx, double vy, double vz)
    {
        if (Physics?.OnVelocityFromServer(this, vx, vy, vz) != true)
        {
            base.SetVelocityClient(vx, vy, vz);
        }
    }

    public override void Move(double dx, double dy, double dz)
    {
        if (Physics?.OnMove(this, dx, dy, dz) != true)
        {
            base.Move(dx, dy, dz);
        }
    }

    public override void AddVelocity(double dx, double dy, double dz)
    {
        if (Physics?.OnAddVelocity(this, dx, dy, dz) != true)
        {
            base.AddVelocity(dx, dy, dz);
        }
    }

    protected override void ReadNbt(NBTTagCompound nbt)
    {
    }

    protected override void WriteNbt(NBTTagCompound nbt)
    {
    }
}
