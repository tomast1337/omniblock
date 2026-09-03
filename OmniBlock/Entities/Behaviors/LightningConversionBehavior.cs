namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     Replaces the struck mob with another type — a pig becoming a zombie pigman. Fully handles the
///     strike, so a converted mob does not also burn.
/// </summary>
public sealed class LightningConversionBehavior(string becomes) : IEntityLifecycle
{
    public bool OnStruckByLightning(EntityLiving self, Entity bolt)
    {
        if (self.World.IsRemote)
        {
            return true;
        }

        Entity replacement = self.World.Content.EntityTypes.Create(becomes, self.World);
        replacement.SetPositionAndAnglesKeepPrevAngles(self.X, self.Y, self.Z, self.Yaw, self.Pitch);
        self.World.SpawnEntity(replacement);
        self.MarkDead();
        return true;
    }
}
