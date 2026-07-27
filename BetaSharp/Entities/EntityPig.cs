using BetaSharp.Items;
using BetaSharp.NBT;
using BetaSharp.Util;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityPig : EntityAnimal
{
    public readonly SyncedProperty<bool> Saddled;

    public EntityPig(IWorldContext world) : base(world, EntityRegistry.ByName("pig").RequireDefinition())
    {
        Saddled = DataSynchronizer.MakeProperty(16, false);

        // One pool; the entry itself picks raw or cooked from the pig's burning state.
    }

    protected override void WriteNbt(NBTTagCompound nbt)
    {
        base.WriteNbt(nbt);
        nbt.SetBoolean("Saddle", Saddled.Value);
    }

    protected override void ReadNbt(NBTTagCompound nbt)
    {
        base.ReadNbt(nbt);
        Saddled.Value = nbt.GetBoolean("Saddle");
    }

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
