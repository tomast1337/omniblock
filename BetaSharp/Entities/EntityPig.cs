using BetaSharp.Entities.State;
using BetaSharp.Items;
using BetaSharp.NBT;
using BetaSharp.Util;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityPig : EntityAnimal
{
    public EntityPig(IWorldContext world) : base(world, EntityRegistry.ByName("pig").RequireDefinition())
    {
        Saddled = DataSynchronizer.Get<bool>(SyncedPropertyFactory.Resolve<bool>(Definition, "saddled").Id);
    }

    /// <summary>Declared in pig.json, including its NBT key — no persistence override needed.</summary>
    public SyncedProperty<bool> Saddled { get; }



    public override bool Interact(EntityPlayer player)
    {
        if (!Saddled.Value || World.IsRemote || (Passenger != null && !Equals(Passenger, player))) return false;

        player.SetVehicle(this);
        return true;
    }

    protected override void OnLanding(float fallDistance)
    {
        base.OnLanding(fallDistance);
        if (fallDistance > 5.0F && Passenger is EntityPlayer player)
        {
            player.IncrementStat(Achievements.KillPig);
        }
    }
}
