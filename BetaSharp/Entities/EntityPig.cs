using BetaSharp.Entities.Behaviors;
using BetaSharp.Items;
using BetaSharp.Loot;
using BetaSharp.NBT;
using BetaSharp.Util;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Entities;

public class EntityPig : EntityAnimal
{
    private static readonly Item s_porkchopCooked = Item.ByName("porkchop_cooked");
    private static readonly Item s_porkchopRaw = Item.ByName("porkchop_raw");
    public readonly SyncedProperty<bool> Saddled;

    public EntityPig(IWorldContext world) : base(world, MobDefinitions.Pig)
    {
        Saddled = DataSynchronizer.MakeProperty(16, false);

        // One pool; the entry itself picks raw or cooked from the pig's burning state.
        Loot = new LootTableBehavior(new LootTable(
            new LootPool([new LootEntry(_ => new ItemStack(IsOnFire ? s_porkchopCooked : s_porkchopRaw, 1))], 0, 2)));
        Lifecycle = new PigLightningBehavior();
    }

    public override EntityType Type => EntityRegistry.Pig;

    protected sealed override void SetBoundingBoxSpacing(float widthOffset, float heightOffset) => base.SetBoundingBoxSpacing(widthOffset, heightOffset);

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
