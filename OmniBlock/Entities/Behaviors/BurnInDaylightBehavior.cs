using OmniBlock.Util.Maths;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     Sets the mob alight when it stands in daylight under open sky. Stateless, and shared by
///     zombies and skeletons.
/// </summary>
public sealed class BurnInDaylightBehavior(int fireTicks = 300) : IEntityTicker
{
    public void OnTickMovement(EntityLiving self)
    {
        if (!self.World.Environment.CanMonsterSpawn())
        {
            return;
        }

        var brightness = self.GetBrightnessAtEyes(1.0F);
        if (brightness > 0.5F
            && self.World.Lighting.HasSkyLight(MathHelper.Floor(self.X), MathHelper.Floor(self.Y), MathHelper.Floor(self.Z))
            && self.Random.NextFloat() * 30.0F < (brightness - 0.4F) * 2.0F)
        {
            self.FireTicks = fireTicks;
        }
    }
}
