using OmniBlock.Blocks.Materials;
using OmniBlock.Entities.State;
using OmniBlock.Util.Maths;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     A squid's swimming: beats its tentacles on a sine cycle, jets along a heading it re-picks at
///     random, and coasts on the velocity that produces. One behavior across Physics and Ticker,
///     because the motion and the animation read the same counters: the mob moves because it is
///     mid-beat.
///     <para>
///         Out of water none of it applies: the squid stops steering, falls, and tips onto its side.
///     </para>
/// </summary>
public sealed class JetSwimBehavior : IEntityPhysics, IEntityTicker
{
    private readonly StateHandle<float> _animationSpeed;
    private readonly StateHandle<float> _beatStrength;
    private readonly StateHandle<float> _headingX;
    private readonly StateHandle<float> _headingY;
    private readonly StateHandle<float> _headingZ;
    private readonly StateHandle<float> _jetSpeed;

    private readonly float _jetStrength;
    private readonly StateHandle<float> _previousTentaclePhase;
    private readonly StateHandle<float> _previousTentacleSpread;
    private readonly StateHandle<float> _previousTiltAngle;
    private readonly int _rerollChanceOneIn;
    private readonly StateHandle<float> _swimPhase;
    private readonly StateHandle<float> _tentaclePhase;
    private readonly StateHandle<float> _tentacleSpread;
    private readonly StateHandle<float> _tiltAngle;
    private readonly double _waterProbeDepth;

    public JetSwimBehavior(in EntityBehaviorContext context)
    {
        _jetStrength = context.Float("jet_strength", 0.2F);
        _rerollChanceOneIn = context.Int("reroll_chance_one_in", 50);
        _waterProbeDepth = context.Double("water_probe_depth", 0.6D);

        _animationSpeed = context.DeclareFloat();
        _swimPhase = context.DeclareFloat();
        _jetSpeed = context.DeclareFloat();
        _beatStrength = context.DeclareFloat();
        _headingX = context.DeclareFloat();
        _headingY = context.DeclareFloat();
        _headingZ = context.DeclareFloat();
        _tentaclePhase = context.DeclareFloat();
        _previousTentaclePhase = context.DeclareFloat();
        _tentacleSpread = context.DeclareFloat();
        _previousTentacleSpread = context.DeclareFloat();
        _tiltAngle = context.DeclareFloat();
        _previousTiltAngle = context.DeclareFloat();
    }

    /// <summary>
    ///     Probes a box reaching below the mob rather than the mob's own, and is carried by the
    ///     current in the same call.
    /// </summary>
    public bool? IsInWater(Entity self) =>
        self.World.Reader.UpdateMovementInFluid(self.BoundingBox.Expand(0.0D, -_waterProbeDepth, 0.0D), Material.Water, self);

    /// <summary>The beat sets velocity outright, so there is nothing to accelerate or damp.</summary>
    public bool Travel(EntityLiving self, float strafe, float forward)
    {
        self.Move(self.VelocityX, self.VelocityY, self.VelocityZ);
        return true;
    }

    public void AfterTickMovement(EntityLiving self)
    {
        var state = self.State;
        state[_previousTiltAngle] = state[_tiltAngle];
        state[_previousTentaclePhase] = state[_tentaclePhase];
        state[_previousTentacleSpread] = state[_tentacleSpread];

        AdvancePhase(self, state);

        if (self.IsInWater)
        {
            SwimStroke(self, state);
        }
        else
        {
            Sink(self, state);
        }
    }

    /// <summary>Re-aims every so often, and always when stalled or out of water.</summary>
    public bool OnTickLiving(EntityLiving self)
    {
        var state = self.State;
        var stalled = state[_headingX] == 0.0F && state[_headingY] == 0.0F && state[_headingZ] == 0.0F;

        // The plain flag, not the probing one: re-aiming must not push the mob.
        if (self.Random.NextInt(_rerollChanceOneIn) == 0 || !self.InWater || stalled)
        {
            var angle = self.Random.NextFloat() * (float)Math.PI * 2.0F;
            state[_headingX] = MathHelper.Cos(angle) * _jetStrength;
            state[_headingY] = -_jetStrength / 2.0F + self.Random.NextFloat() * _jetStrength;
            state[_headingZ] = MathHelper.Sin(angle) * _jetStrength;
        }

        self.TickDespawn();

        // The mob's whole AI: no pathing, no looking around, no ageing.
        return true;
    }

    /// <summary>
    ///     Walks the beat cycle round, re-rolling how fast the next cycles run one time in ten.
    ///     Seeded on first use, not at construction: behaviors are shared across every mob of the
    ///     type, so a state default cannot be random.
    /// </summary>
    private void AdvancePhase(EntityLiving self, EntityState state)
    {
        if (state[_animationSpeed] == 0.0F)
        {
            state[_animationSpeed] = RollAnimationSpeed(self);
        }

        state[_swimPhase] += state[_animationSpeed];
        if (state[_swimPhase] <= (float)Math.PI * 2.0F)
        {
            return;
        }

        state[_swimPhase] -= (float)Math.PI * 2.0F;
        if (self.Random.NextInt(10) == 0)
        {
            state[_animationSpeed] = RollAnimationSpeed(self);
        }
    }

    private static float RollAnimationSpeed(EntityLiving self) => 1.0F / (self.Random.NextFloat() + 1.0F) * 0.2F;

    /// <summary>
    ///     The first half of the cycle spreads the tentacles and, at the end of the spread, kicks;
    ///     the second half coasts with the tentacles closed.
    /// </summary>
    private void SwimStroke(EntityLiving self, EntityState state)
    {
        float phaseProgress;
        if (state[_swimPhase] < (float)Math.PI)
        {
            phaseProgress = state[_swimPhase] / (float)Math.PI;
            state[_tentacleSpread] = MathHelper.Sin(phaseProgress * phaseProgress * (float)Math.PI) * (float)Math.PI * 0.25F;
            if (phaseProgress > 0.75D)
            {
                state[_jetSpeed] = 1.0F;
                state[_beatStrength] = 1.0F;
            }
            else
            {
                state[_beatStrength] *= 0.8F;
            }
        }
        else
        {
            state[_tentacleSpread] = 0.0F;
            state[_jetSpeed] *= 0.9F;
            state[_beatStrength] *= 0.99F;
        }

        if (!self.InterpolateOnly)
        {
            self.VelocityX = state[_headingX] * state[_jetSpeed];
            self.VelocityY = state[_headingY] * state[_jetSpeed];
            self.VelocityZ = state[_headingZ] * state[_jetSpeed];
        }

        phaseProgress = MathHelper.Sqrt(self.VelocityX * self.VelocityX + self.VelocityZ * self.VelocityZ);
        self.BodyYaw += (-(float)Math.Atan2(self.VelocityX, self.VelocityZ) * 180.0F / (float)Math.PI - self.BodyYaw) * 0.1F;
        self.Yaw = self.BodyYaw;
        state[_tentaclePhase] += (float)Math.PI * state[_beatStrength] * 1.5F;
        state[_tiltAngle] += (-(float)Math.Atan2(phaseProgress, self.VelocityY) * 180.0F / (float)Math.PI - state[_tiltAngle]) * 0.1F;
    }

    /// <summary>Out of water the beat is cosmetic; the mob falls and rolls onto its side.</summary>
    private void Sink(EntityLiving self, EntityState state)
    {
        state[_tentacleSpread] = MathHelper.Abs(MathHelper.Sin(state[_swimPhase])) * (float)Math.PI * 0.25F;

        if (!self.InterpolateOnly)
        {
            self.VelocityX = 0.0D;
            self.VelocityY -= 0.08D;
            self.VelocityY *= 0.98F;
            self.VelocityZ = 0.0D;
        }

        state[_tiltAngle] = (float)(state[_tiltAngle] + (-90.0F - state[_tiltAngle]) * 0.02D);
    }

    /// <summary>Interpolated animation values for the renderer, which finds this by capability.</summary>
    public float TentacleSpread(Entity self, float tickDelta) => Interpolate(self, _previousTentacleSpread, _tentacleSpread, tickDelta);

    public float TentaclePhase(Entity self, float tickDelta) => Interpolate(self, _previousTentaclePhase, _tentaclePhase, tickDelta);

    public float TiltAngle(Entity self, float tickDelta) => Interpolate(self, _previousTiltAngle, _tiltAngle, tickDelta);

    private static float Interpolate(Entity self, StateHandle<float> previous, StateHandle<float> current, float tickDelta)
    {
        var state = self.State;
        return state[previous] + (state[current] - state[previous]) * tickDelta;
    }
}
