namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Replaces the struck mob with a zombie pigman. Fully handles the strike — the default fire and
///     damage response never runs, so a converted pig does not also burn.
/// </summary>
public sealed class PigLightningBehavior : IEntityLifecycle
{
    public bool OnStruckByLightning(EntityLiving self, EntityLightningBolt bolt)
    {
        if (self.World.IsRemote) return true;

        EntityPigZombie pigZombie = new(self.World);
        pigZombie.SetPositionAndAnglesKeepPrevAngles(self.X, self.Y, self.Z, self.Yaw, self.Pitch);
        self.World.SpawnEntity(pigZombie);
        self.MarkDead();
        return true;
    }
}
