using OmniBlock.Entities.State;
using OmniBlock.Util.Maths;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     A slime's whole way of moving: faces the nearest player, hops at it on a timer that shortens
///     when someone is near, and splats on landing. One behavior across Ticker and Lifecycle, because
///     the hop, the landing and the squash the renderer draws are one motion.
///     <para>
///         Everything scales with the mob's synced <c>size</c>: a bigger slime hops further, lands
///         harder and is heard doing it.
///     </para>
/// </summary>
public sealed class HoppingBehavior : IEntityTicker, IEntityLifecycle
{
    private readonly int _delayMinimum;
    private readonly int _delaySpread;
    private readonly StateHandle<int> _jumpDelay;
    private readonly int _jumpSoundMinimumSize;
    private readonly int _landingSoundMinimumSize;
    private readonly int _nearbyDelayDivisor;
    private readonly double _noticeRange;
    private readonly string _particle;
    private readonly int _particlesPerSize;
    private readonly StateHandle<float> _previousSquish;
    private readonly string _sound;
    private readonly StateHandle<float> _squish;
    private readonly float _squishDecay;
    private readonly StateHandle<bool> _wasOnGround;

    public HoppingBehavior(in EntityBehaviorContext context)
    {
        _delayMinimum = context.Int("delay_minimum", 10);
        _delaySpread = context.Int("delay_spread", 20);
        _nearbyDelayDivisor = context.Int("nearby_delay_divisor", 3);
        _noticeRange = context.Double("notice_range", 16.0D);
        _squishDecay = context.Float("squish_decay", 0.6F);
        _particle = context.Json.GetProperty("particle").GetString()!;
        _particlesPerSize = context.Int("particles_per_size", 8);
        _sound = context.Json.GetProperty("sound").GetString()!;
        _jumpSoundMinimumSize = context.Int("jump_sound_minimum_size", 2);
        _landingSoundMinimumSize = context.Int("landing_sound_minimum_size", 3);

        _jumpDelay = context.DeclareInt();
        _squish = context.DeclareFloat();
        _previousSquish = context.DeclareFloat();
        _wasOnGround = context.DeclareBool();
    }

    public void OnCreated(Entity self) => self.State[_jumpDelay] = RollDelay(self);

    /// <summary>Snapshots the squash and the footing the landing test compares against.</summary>
    public void OnTick(Entity self)
    {
        self.State[_previousSquish] = self.State[_squish];
        self.State[_wasOnGround] = self.OnGround;
    }

    /// <summary>Runs after movement has resolved, which is when a landing has actually happened.</summary>
    public void OnTickEnd(EntityLiving self)
    {
        EntityState state = self.State;
        if (self.OnGround && !state[_wasOnGround])
        {
            Splat(self, state);
        }

        state[_squish] *= _squishDecay;
    }

    public bool OnTickLiving(EntityLiving self)
    {
        self.TickDespawn();

        EntityState state = self.State;
        EntityPlayer? player = self.World.Entities.GetClosestPlayerTarget(self.X, self.Y, self.Z, _noticeRange);
        if (player != null)
        {
            self.faceEntity(player, 10.0F, 20.0F);
        }

        if (self.OnGround && state[_jumpDelay]-- <= 0)
        {
            Hop(self, state, player);
        }
        else
        {
            Settle(self);
        }

        // Hopping is the mob's whole AI: no pathing, no idle glancing, no ageing.
        return true;
    }

    private static int Size(Entity self) => self.Synced<byte>("size")?.Value ?? 1;

    private int RollDelay(Entity self) => self.Random.NextInt(_delaySpread) + _delayMinimum;

    private void Splat(EntityLiving self, EntityState state)
    {
        int size = Size(self);
        for (int particle = 0; particle < size * _particlesPerSize; ++particle)
        {
            float angle = self.Random.NextFloat() * (float)Math.PI * 2.0F;
            float spread = self.Random.NextFloat() * 0.5F + 0.5F;
            float offsetX = MathHelper.Sin(angle) * size * 0.5F * spread;
            float offsetZ = MathHelper.Cos(angle) * size * 0.5F * spread;
            self.World.Broadcaster.AddParticle(_particle, self.X + offsetX, self.BoundingBox.MinY, self.Z + offsetZ, 0.0D, 0.0D, 0.0D);
        }

        if (size >= _landingSoundMinimumSize)
        {
            PlaySound(self, 1.0F / 0.8F);
        }

        state[_squish] = -0.5F;
    }

    private void Hop(EntityLiving self, EntityState state, EntityPlayer? player)
    {
        state[_jumpDelay] = RollDelay(self);
        if (player != null)
        {
            state[_jumpDelay] /= _nearbyDelayDivisor;
        }

        int size = Size(self);
        self.Jumping = true;
        if (size >= _jumpSoundMinimumSize)
        {
            PlaySound(self, 0.8F);
        }

        state[_squish] = 1.0F;
        self.SidewaysSpeed = 1.0F - self.Random.NextFloat() * 2.0F;
        self.ForwardSpeed = size;
    }

    private static void Settle(EntityLiving self)
    {
        self.Jumping = false;
        if (self.OnGround)
        {
            self.SidewaysSpeed = self.ForwardSpeed = 0.0F;
        }
    }

    private void PlaySound(EntityLiving self, float pitchScale) =>
        self.World.Broadcaster.PlaySoundAtEntity(self, _sound, self.SoundVolume, ((self.Random.NextFloat() - self.Random.NextFloat()) * 0.2F + 1.0F) * pitchScale);

    /// <summary>Interpolated squash for the renderer, which finds this behavior by capability.</summary>
    public float Squish(Entity self, float tickDelta)
    {
        EntityState state = self.State;
        return state[_previousSquish] + (state[_squish] - state[_previousSquish]) * tickDelta;
    }
}
