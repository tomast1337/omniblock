namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Inverts the usual monster pathing preference so the mob seeks out lit ground instead of
///     avoiding it — the giant, which is too big to care about being seen.
/// </summary>
public sealed class LightSeekingPathBehavior : IEntityPhysics
{
    public float? GetBlockPathWeight(EntityLiving self, int x, int y, int z) =>
        self.World.Lighting.GetLuminance(x, y, z) - 0.5F;
}
