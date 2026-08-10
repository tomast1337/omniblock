namespace OmniBlock.Entities.Behaviors;

/// <summary>Hunts the closest targetable player within <paramref name="radius" /> that the mob can see.</summary>
public sealed class AlwaysHuntTargetBehavior(double radius = 16.0D) : IEntityTargetBehavior
{
    public Entity? FindPlayerToAttack(EntityCreature self)
    {
        EntityPlayer? player = self.World.Entities.GetClosestPlayerTarget(self.X, self.Y, self.Z, radius);
        return player != null && self.CanSee(player) ? player : null;
    }
}
