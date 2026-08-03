using BetaSharp.Entities.State;
using BetaSharp.Util.Maths;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     The ghast's attack: picks the nearest player, turns to face them, and winds up a fireball
///     while they stay in sight. Losing sight unwinds the charge instead of cancelling it, so a
///     target that ducks behind a wall and back out is fired on sooner.
///     <para>
///         The charge counter is per-mob state. Whether it has passed the halfway mark is a synced
///         property, because the client swaps the mob's texture on it. The renderer reads the raw
///         counter back through <see cref="ChargeProgress" /> to scale the model.
///     </para>
/// </summary>
public sealed class FireballAttackBehavior : IEntityTicker
{
    private readonly StateHandle<int> _aggroCooldown;
    private readonly int _aggroTicks;
    private readonly double _attackRange;
    private readonly StateHandle<int> _charge;
    private readonly string _chargeSound;
    private readonly int _chargeTicks;
    private readonly SyncedHandle<bool> _charging;
    private readonly string _chargingTexture;
    private readonly string _fireSound;
    private readonly StateHandle<int> _previousCharge;
    private readonly int _reloadTicks;

    private readonly double _searchRange;
    private readonly double _spawnOffset;
    private readonly StateHandle<Entity> _target;

    public FireballAttackBehavior(in EntityBehaviorContext context)
    {
        _searchRange = context.Double("search_range", 100.0D);
        _attackRange = context.Double("attack_range", 64.0D);
        _aggroTicks = context.Int("aggro_ticks", 20);
        _chargeTicks = context.Int("charge_ticks", 20);
        _reloadTicks = context.Int("reload_ticks", 40);
        _spawnOffset = context.Double("spawn_offset", 4.0D);
        _chargeSound = context.Json.GetProperty("charge_sound").GetString()!;
        _fireSound = context.Json.GetProperty("fire_sound").GetString()!;
        _chargingTexture = context.Json.GetProperty("charging_texture").GetString()!;

        _target = context.DeclareRef<Entity>();
        _aggroCooldown = context.DeclareInt();
        _charge = context.DeclareInt();
        _previousCharge = context.DeclareInt();
        _charging = context.Synced<bool>("charging");
    }

    public bool OnTickLiving(EntityLiving self)
    {
        EntityState state = self.State;
        state[_previousCharge] = state[_charge];

        Entity? target = Reacquire(self);
        if (target != null && target.GetSquaredDistance(self) < _attackRange * _attackRange)
        {
            AimAt(self, target);
        }
        else
        {
            // No target, so the mob looks the way it is drifting.
            self.BodyYaw = self.Yaw = -(float)Math.Atan2(self.VelocityX, self.VelocityZ) * 180.0F / (float)Math.PI;
            Unwind(self);
        }

        if (!self.World.IsRemote)
        {
            self.DataSynchronizer.Get<bool>(_charging.Id).Value = state[_charge] > _chargeTicks / 2;
        }

        // The wander behavior beside this one is the mob's AI; this adds to it.
        return false;
    }

    /// <summary>Swaps in the charging texture, the only thing the client does with the synced flag.</summary>
    public void OnTickEnd(EntityLiving self) =>
        self.Texture = self.DataSynchronizer.Get<bool>(_charging.Id).Value ? _chargingTexture : self.Definition.Texture;

    /// <summary>
    ///     Keeps the current target until it dies or the cooldown lapses, then takes whichever player
    ///     is nearest. The cooldown means a ghast does not re-scan the world every tick.
    /// </summary>
    private Entity? Reacquire(EntityLiving self)
    {
        EntityState state = self.State;
        Entity? target = state.GetRef(_target);

        if (target is { Dead: true })
        {
            target = null;
            state.SetRef(_target, null);
        }

        if (target != null && state[_aggroCooldown]-- > 0)
        {
            return target;
        }

        target = self.World.Entities.GetClosestPlayerTarget(self.X, self.Y, self.Z, _searchRange);
        state.SetRef(_target, target);
        if (target != null)
        {
            state[_aggroCooldown] = _aggroTicks;
        }

        return target;
    }

    private void AimAt(EntityLiving self, Entity target)
    {
        double dx = target.X - self.X;
        double dy = target.BoundingBox.MinY + target.Height / 2.0F - (self.Y + self.Height / 2.0F);
        double dz = target.Z - self.Z;
        self.BodyYaw = self.Yaw = -(float)Math.Atan2(dx, dz) * 180.0F / (float)Math.PI;

        if (!self.CanSee(target))
        {
            Unwind(self);
            return;
        }

        EntityState state = self.State;
        if (state[_charge] == _chargeTicks / 2)
        {
            PlaySound(self, _chargeSound);
        }

        ++state[_charge];
        if (state[_charge] != _chargeTicks)
        {
            return;
        }

        PlaySound(self, _fireSound);
        Fire(self, dx, dy, dz);
        state[_charge] = -_reloadTicks;
    }

    private void Fire(EntityLiving self, double dx, double dy, double dz)
    {
        Entity fireball = FireballBehavior.Shoot(self.World, self, dx, dy, dz);
        Vec3D look = self.GetLook(1.0F);
        fireball.X = self.X + look.X * _spawnOffset;
        fireball.Y = self.Y + self.Height / 2.0F + 0.5D;
        fireball.Z = self.Z + look.Z * _spawnOffset;
        self.World.SpawnEntity(fireball);
    }

    /// <summary>
    ///     Winds the charge back down, but never past zero: a mob still serving its reload penalty
    ///     keeps counting up towards zero instead of being pushed further below it.
    /// </summary>
    private void Unwind(EntityLiving self)
    {
        if (self.State[_charge] > 0)
        {
            --self.State[_charge];
        }
    }

    private static void PlaySound(EntityLiving self, string sound) =>
        self.World.Broadcaster.PlaySoundAtEntity(self, sound, self.SoundVolume, (self.Random.NextFloat() - self.Random.NextFloat()) * 0.2F + 1.0F);

    /// <summary>
    ///     Interpolated charge as a fraction of a full wind-up, floored at zero so the reload penalty
    ///     reads as "not charging". The renderer scales the model by it.
    /// </summary>
    public float ChargeProgress(Entity self, float tickDelta)
    {
        EntityState state = self.State;
        float progress = (state[_previousCharge] + (state[_charge] - state[_previousCharge]) * tickDelta) / _chargeTicks;
        return progress < 0.0F ? 0.0F : progress;
    }
}
