namespace BetaSharp.Entities.Behaviors;

/// <summary>
///     Occasionally spawns another entity riding this one, as a skeleton rides a spider. Both the
///     rider and the odds are declared, so the pairing is data.
/// </summary>
public sealed class SpawnRiderBehavior(string rider, int chanceOneIn) : IEntityLifecycle
{
    public void OnPostSpawn(EntityLiving self)
    {
        if (self.World.Random.NextInt(chanceOneIn) != 0)
        {
            return;
        }

        Entity mount = EntityRegistry.ByName(rider).Create(self.World);
        mount.SetPositionAndAnglesKeepPrevAngles(self.X, self.Y, self.Z, self.Yaw, 0.0F);
        self.World.SpawnEntity(mount);
        mount.SetVehicle(self);
    }
}
