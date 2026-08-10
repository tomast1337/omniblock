using System.Text.Json;
using OmniBlock.Entities.State;

namespace OmniBlock.Entities.Behaviors;

/// <summary>
///     Mounts the player when the entity is saddled and not already carrying someone else. The
///     saddle flag is read through a handle resolved from the entity's declared synced properties.
/// </summary>
public sealed class RideIfSaddledBehavior : IEntityInteractable
{
    private readonly SyncedHandle<bool> _saddled;

    public RideIfSaddledBehavior(in EntityBehaviorContext context) =>
        _saddled = context.Synced<bool>(context.Json.TryGetProperty("saddled_property", out JsonElement name)
            ? name.GetString() ?? "saddled"
            : "saddled");

    public bool OnInteract(Entity self, EntityPlayer player)
    {
        if (!self.DataSynchronizer.Get<bool>(_saddled.Id).Value)
        {
            return false;
        }

        if (self.World.IsRemote)
        {
            return false;
        }

        if (self.Passenger != null && !Equals(self.Passenger, player))
        {
            return false;
        }

        player.SetVehicle(self);
        return true;
    }
}
