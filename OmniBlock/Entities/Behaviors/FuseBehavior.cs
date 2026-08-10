using System.Text.Json;
using OmniBlock.Entities.State;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     A creeper's fuse: winds up while a target is in range, winds back down when the target is
///     lost or hidden, and detonates at full charge. One behavior across the Attack, Ticker and
///     Lifecycle slots, because all three hooks move the same countdown.
///     <para>
///         The countdown is per-creeper so it lives in <see cref="EntityState" />. The direction
///         (<c>1</c> winding up, <c>-1</c> as an unsigned <c>255</c> winding down) is a synced byte,
///         because the client drives the swell animation from it.
///     </para>
/// </summary>
public sealed class FuseBehavior : IEntityAttackBehavior, IEntityTicker, IEntityLifecycle
{
    private const byte WindingUp = 1;
    private const byte WindingDown = 255;
    private readonly float _armedRange;

    private readonly StateHandle<int> _charge;
    private readonly string _fuseSound;

    private readonly int _fuseTicks;
    private readonly float _power;
    private readonly SyncedHandle<bool> _powered;
    private readonly float _poweredPower;
    private readonly StateHandle<int> _previousCharge;
    private readonly SyncedHandle<byte> _state;
    private readonly float _triggerRange;

    public FuseBehavior(in EntityBehaviorContext context)
    {
        _fuseTicks = context.Int("fuse_ticks", 30);
        _triggerRange = context.Float("trigger_range", 3.0F);
        _armedRange = context.Float("armed_range", 7.0F);
        _power = context.Float("power", 3.0F);
        _poweredPower = context.Float("powered_power", 6.0F);
        _fuseSound = context.Json.TryGetProperty("fuse_sound", out JsonElement s)
            ? s.GetString() ?? "random.fuse"
            : "random.fuse";

        _charge = context.DeclareInt();
        _previousCharge = context.DeclareInt();
        _state = context.Synced<byte>("state");
        _powered = context.Synced<bool>("powered");
    }

    public void AttackEntity(EntityCreature self, Entity target, float distance)
    {
        if (self.World.IsRemote)
        {
            return;
        }

        // Once lit, the creeper keeps closing from further away than it took to light it.
        float range = Step(State(self)) <= 0 ? _triggerRange : _armedRange;
        if (!(distance < range))
        {
            WindDown(self);
            return;
        }

        if (self.State[_charge] == 0)
        {
            self.World.Broadcaster.PlaySoundAtEntity(self, _fuseSound, 1.0F, 0.5F);
        }

        SetState(self, WindingUp);
        if (++self.State[_charge] >= _fuseTicks)
        {
            float power = self.DataSynchronizer.Get<bool>(_powered.Id).Value ? _poweredPower : _power;
            self.World.CreateExplosion(self, self.X, self.Y, self.Z, power);
            self.MarkDead();
        }

        self.HasAttacked = true;
    }

    /// <summary>With the target behind cover the fuse burns down instead of holding its charge.</summary>
    public void AttackBlockedEntity(EntityCreature self, Entity target, float distance)
    {
        if (self.World.IsRemote || self.State[_charge] <= 0)
        {
            return;
        }

        WindDown(self);
    }

    /// <summary>
    ///     Lightning supercharges the creeper. Returns <c>false</c> so the default strike response
    ///     still runs on top.
    /// </summary>
    public bool OnStruckByLightning(EntityLiving self, Entity bolt)
    {
        self.DataSynchronizer.Get<bool>(_powered.Id).Value = true;
        return false;
    }

    /// <summary>
    ///     Snapshots the charge for interpolation and advances the client-side swell. Runs before
    ///     movement and AI, so the snapshot is taken before anything can move the fuse this tick.
    /// </summary>
    public void OnTick(Entity self)
    {
        self.State[_previousCharge] = self.State[_charge];
        if (!self.World.IsRemote)
        {
            return;
        }

        int step = Step(State(self));
        if (step > 0 && self.State[_charge] == 0)
        {
            self.World.Broadcaster.PlaySoundAtEntity(self, _fuseSound, 1.0F, 0.5F);
        }

        self.State[_charge] = Math.Clamp(self.State[_charge] + step, 0, _fuseTicks);
    }

    /// <summary>Losing the target lets the fuse burn back down.</summary>
    public void OnTickEnd(EntityLiving self)
    {
        if (self.World.IsRemote || self is not EntityCreature { Target: null })
        {
            return;
        }

        if (self.State[_charge] <= 0)
        {
            return;
        }

        WindDown(self);
    }

    private byte State(Entity self) => self.DataSynchronizer.Get<byte>(_state.Id).Value;
    private void SetState(Entity self, byte value) => self.DataSynchronizer.Get<byte>(_state.Id).Value = value;

    /// <summary>Fuse direction arrives as an unsigned byte but is read as a signed step.</summary>
    private static int Step(byte state) => (sbyte)state;

    private void WindDown(Entity self)
    {
        SetState(self, WindingDown);
        self.State[_charge] = Math.Max(self.State[_charge] - 1, 0);
    }

    /// <summary>
    ///     Interpolated fuse charge in 0..1-ish, for the renderer's swell. Divided by
    ///     <c>fuseTicks - 2</c> so the creeper is still growing at the moment it detonates.
    /// </summary>
    public float FlashTime(Entity self, float tickDelta)
    {
        int previous = self.State[_previousCharge];
        return (previous + (self.State[_charge] - previous) * tickDelta) / (_fuseTicks - 2.0F);
    }
}
