using BetaSharp.Entities.State;
using BetaSharp.Network.Packets.S2CPlay;
using BetaSharp.Util.Maths;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     A soaked mob shakes itself dry the moment it finds dry ground to stand on: it stops where it
///     is, sprays water, and cannot be hurried. Getting wet again at any point restarts the wait.
///     <para>
///         Two separate facts, not one: that it <em>needs</em> to shake, and that it is shaking now.
///         The first survives being in the water; the second only begins on land.
///     </para>
/// </summary>
public sealed class ShakeOffWaterBehavior : IEntityPhysics, IEntityTicker, IEntityLifecycle
{
    private readonly StateHandle<bool> _needsShake;
    private readonly StateHandle<bool> _shaking;
    private readonly StateHandle<float> _shakeTime;
    private readonly StateHandle<float> _previousShakeTime;

    private readonly string _sound;
    private readonly string _particle;
    private readonly float _speed;
    private readonly float _duration;
    private readonly float _sprayStart;

    public ShakeOffWaterBehavior(in EntityBehaviorContext context)
    {
        _sound = context.Json.GetProperty("sound").GetString()!;
        _particle = context.Json.GetProperty("particle").GetString()!;
        _speed = context.Float("speed", 0.05F);
        _duration = context.Float("duration", 2.0F);
        _sprayStart = context.Float("spray_start", 0.4F);

        _needsShake = context.DeclareBool();
        _shaking = context.DeclareBool();
        _shakeTime = context.DeclareFloat();
        _previousShakeTime = context.DeclareFloat();
    }

    public bool IsShaking(Entity self) => self.State[_needsShake];

    /// <summary>A mob mid-shake plants itself; it will not be moved until it is done.</summary>
    public bool? IsMovementCeased(EntityLiving self) => self.State[_shaking] ? true : null;

    /// <summary>Starts the shake once the mob is soaked, still, and on the ground.</summary>
    public void AfterTickMovement(EntityLiving self)
    {
        EntityState state = self.State;
        if (self.InterpolateOnly || !state[_needsShake] || state[_shaking]) return;
        if (self is EntityCreature { HasPath: true } || !self.OnGround) return;

        Begin(self);
        self.World.Broadcaster.EntityEvent(self, EntityStatusS2CPacket.EntityState.WolfShaking);
    }

    private void Begin(EntityLiving self)
    {
        EntityState state = self.State;
        state[_shaking] = true;
        state[_shakeTime] = 0.0F;
        state[_previousShakeTime] = 0.0F;
    }

    public void OnTickEnd(EntityLiving self)
    {
        EntityState state = self.State;

        if (self.IsWet)
        {
            // Soaked again: whatever shake was under way is abandoned and the need is renewed.
            state[_needsShake] = true;
            state[_shaking] = false;
            state[_shakeTime] = 0.0F;
            state[_previousShakeTime] = 0.0F;
            return;
        }

        if (!state[_shaking]) return;

        if (state[_shakeTime] == 0.0F)
        {
            self.World.Broadcaster.PlaySoundAtEntity(self, _sound, self.SoundVolume, (self.Random.NextFloat() - self.Random.NextFloat()) * 0.2F + 1.0F);
        }

        state[_previousShakeTime] = state[_shakeTime];
        state[_shakeTime] += _speed;

        if (state[_previousShakeTime] >= _duration)
        {
            state[_needsShake] = false;
            state[_shaking] = false;
            state[_previousShakeTime] = 0.0F;
            state[_shakeTime] = 0.0F;
        }

        if (state[_shakeTime] > _sprayStart) Spray(self, state);
    }

    private void Spray(EntityLiving self, EntityState state)
    {
        float groundY = (float)self.BoundingBox.MinY;
        int count = (int)(MathHelper.Sin((state[_shakeTime] - _sprayStart) * (float)Math.PI) * 7.0F);

        for (int drop = 0; drop < count; ++drop)
        {
            float offsetX = (self.Random.NextFloat() * 2.0F - 1.0F) * self.Width * 0.5F;
            float offsetZ = (self.Random.NextFloat() * 2.0F - 1.0F) * self.Width * 0.5F;
            self.World.Broadcaster.AddParticle(_particle, self.X + offsetX, groundY + 0.8F, self.Z + offsetZ, self.VelocityX, self.VelocityY, self.VelocityZ);
        }
    }

    /// <summary>The client is told to start shaking rather than working it out for itself.</summary>
    public bool OnEntityStatus(EntityLiving self, sbyte status)
    {
        if ((EntityStatusS2CPacket.EntityState)status != EntityStatusS2CPacket.EntityState.WolfShaking) return false;

        Begin(self);
        return true;
    }

    /// <summary>How dark the mob is drawn while spraying, which peaks with the shake.</summary>
    public float Shading(Entity self, float tickDelta) => 12.0F / 16.0F + Interpolated(self, tickDelta) / 2.0F * 0.25F;

    /// <summary>
    ///     Body-part rotation partway through the shake. The offset staggers the parts so the mob
    ///     ripples from the head back rather than swinging in one piece.
    /// </summary>
    public float ShakeAngle(Entity self, float tickDelta, float offset)
    {
        float progress = Math.Clamp((Interpolated(self, tickDelta) + offset) / 1.8F, 0.0F, 1.0F);
        return MathHelper.Sin(progress * (float)Math.PI) * MathHelper.Sin(progress * (float)Math.PI * 11.0F) * 0.15F * (float)Math.PI;
    }

    private float Interpolated(Entity self, float tickDelta)
    {
        EntityState state = self.State;
        return state[_previousShakeTime] + (state[_shakeTime] - state[_previousShakeTime]) * tickDelta;
    }
}
