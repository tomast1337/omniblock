using BetaSharp.Util.Maths;

namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Sets the mob alight when it stands in daylight under open sky. Stateless — zombies and
///     skeletons carried byte-identical copies of this before it became one shared behavior.
/// </summary>
public sealed class BurnInDaylightBehavior(int fireTicks = 300) : IEntityTicker
{
    public void OnTickMovement(EntityLiving self)
    {
        if (!self.World.Environment.CanMonsterSpawn()) return;

        float brightness = self.GetBrightnessAtEyes(1.0F);
        if (brightness > 0.5F
            && self.World.Lighting.HasSkyLight(MathHelper.Floor(self.X), MathHelper.Floor(self.Y), MathHelper.Floor(self.Z))
            && self.Random.NextFloat() * 30.0F < (brightness - 0.4F) * 2.0F)
        {
            self.FireTicks = fireTicks;
        }
    }
}
