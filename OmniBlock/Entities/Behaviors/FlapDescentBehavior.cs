using OmniBlock.Entities.State;
using OmniBlock.Util.Maths;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     A chicken's wings: they damp its fall, spare it any landing damage, and drive the flap
///     animation the renderer reads back. All three come from the wings, so they are one behavior.
///     <para>
///         The four animation counters are per-chicken, so they live in
///         <see cref="EntityState" /> behind handles resolved once at load.
///     </para>
/// </summary>
public sealed class FlapDescentBehavior : IEntityPhysics
{
    private readonly double _descentDamping;
    private readonly StateHandle<float> _flapProgress;
    private readonly StateHandle<float> _flapSpeed;
    private readonly StateHandle<float> _previousFlapProgress;
    private readonly StateHandle<float> _previousWingExtension;
    private readonly StateHandle<float> _wingExtension;

    public FlapDescentBehavior(in EntityBehaviorContext context)
    {
        _descentDamping = context.Double("descent_damping", 0.6D);
        _flapSpeed = context.DeclareFloat(1.0F);
        _flapProgress = context.DeclareFloat();
        _previousFlapProgress = context.DeclareFloat();
        _wingExtension = context.DeclareFloat();
        _previousWingExtension = context.DeclareFloat();
    }

    /// <summary>Wings absorb the landing: a chicken never takes fall damage.</summary>
    public bool OnLanding(EntityLiving self, float fallDistance) => true;

    public void AfterTickMovement(EntityLiving self)
    {
        var state = self.State;

        if (self.World.IsRemote)
        {
            self.OnGround = Math.Abs(self.Y - self.PrevY) < 0.02D;
        }

        state[_previousFlapProgress] = state[_flapProgress];
        state[_previousWingExtension] = state[_wingExtension];

        var extension = (float)(state[_wingExtension] + (self.OnGround ? -1 : 4) * 0.3D);
        state[_wingExtension] = Math.Clamp(extension, 0.0F, 1.0F);

        if (!self.OnGround && state[_flapSpeed] < 1.0F)
        {
            state[_flapSpeed] = 1.0F;
        }

        state[_flapSpeed] = (float)(state[_flapSpeed] * 0.9D);

        if (!self.OnGround && self.VelocityY < 0.0D)
        {
            self.VelocityY *= _descentDamping;
        }

        state[_flapProgress] += state[_flapSpeed] * 2.0F;
    }

    /// <summary>
    ///     Interpolated wing angle for the renderer, which finds this behavior by capability rather
    ///     than by knowing what a chicken is.
    /// </summary>
    public float WingRotation(Entity self, float tickDelta)
    {
        var state = self.State;
        var flap = state[_previousFlapProgress] + (state[_flapProgress] - state[_previousFlapProgress]) * tickDelta;
        var extension = state[_previousWingExtension] + (state[_wingExtension] - state[_previousWingExtension]) * tickDelta;
        return (MathHelper.Sin(flap) + 1.0F) * extension;
    }
}
