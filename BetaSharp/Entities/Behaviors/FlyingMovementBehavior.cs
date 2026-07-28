using BetaSharp.Blocks;
using BetaSharp.Util.Maths;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Movement for a mob that flies: no gravity, drag on all three axes, and nothing to climb. It
///     also swallows the landing, so a flier that does touch down takes no fall damage.
///     <para>
///         This was the whole of the <c>EntityFlying</c> base class. Flight is a property of how a
///         mob moves, not of what it is, so it is a Physics behavior rather than a place in the
///         hierarchy.
///     </para>
/// </summary>
public sealed class FlyingMovementBehavior : IEntityPhysics
{
    /// <summary>Nothing to grip in the air; a flier never counts as climbing.</summary>
    public bool? IsClimbing(EntityLiving self) => false;

    /// <summary>Landing is handled by doing nothing at all — no damage, no step sound.</summary>
    public bool OnLanding(EntityLiving self, float fallDistance) => true;

    public bool Travel(EntityLiving self, float strafe, float forward)
    {
        if (self.IsInWater)
        {
            self.MoveNonSolid(strafe, forward, 0.02F);
            self.Move(self.VelocityX, self.VelocityY, self.VelocityZ);
            self.VelocityX *= 0.8F;
            self.VelocityY *= 0.8F;
            self.VelocityZ *= 0.8F;
        }
        else if (self.IsTouchingLava)
        {
            self.MoveNonSolid(strafe, forward, 0.02F);
            self.Move(self.VelocityX, self.VelocityY, self.VelocityZ);
            self.VelocityX *= 0.5D;
            self.VelocityY *= 0.5D;
            self.VelocityZ *= 0.5D;
        }
        else
        {
            float friction = GroundFriction(self);
            float accelerationFactor = 0.16277136F / (friction * friction * friction);
            self.MoveNonSolid(strafe, forward, self.OnGround ? 0.1F * accelerationFactor : 0.02F);

            // Re-read rather than reuse: the acceleration above may have moved the mob onto a
            // different block, and the drag applied below is the one under it now.
            friction = GroundFriction(self);
            self.Move(self.VelocityX, self.VelocityY, self.VelocityZ);
            self.VelocityX *= friction;
            self.VelocityY *= friction;
            self.VelocityZ *= friction;
        }

        self.LastWalkAnimationSpeed = self.WalkAnimationSpeed;
        double dx = self.X - self.PrevX;
        double dz = self.Z - self.PrevZ;
        float distanceMoved = MathHelper.Sqrt(dx * dx + dz * dz) * 4.0F;
        if (distanceMoved > 1.0F)
        {
            distanceMoved = 1.0F;
        }

        self.WalkAnimationSpeed += (distanceMoved - self.WalkAnimationSpeed) * 0.4F;
        self.AnimationPhase += self.WalkAnimationSpeed;
        return true;
    }

    private static float GroundFriction(EntityLiving self)
    {
        if (!self.OnGround) return 0.91F;

        int groundBlockId = self.World.Reader.GetBlockId(
            MathHelper.Floor(self.X),
            MathHelper.Floor(self.BoundingBox.MinY) - 1,
            MathHelper.Floor(self.Z));

        return groundBlockId > 0 ? Block.Blocks[groundBlockId].slipperiness * 0.91F : 546.0F * 0.1F * 0.1F * 0.1F;
    }
}
