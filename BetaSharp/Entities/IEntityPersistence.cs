using OmniBlock.NBT;

namespace OmniBlock.Entities;

/// <summary>
///     Composable NBT persistence, for state a declared synced property cannot express: packed bit
///     fields, values whose setter has side effects, plain unsynced fields.
///     <para>
///         A synced property that maps one-to-one onto an NBT key does not need this: give it an
///         <c>Nbt</c> key in the entity's JSON and it round-trips automatically.
///     </para>
/// </summary>
public interface IEntityPersistence
{
    void OnWriteNbt(Entity self, NBTTagCompound nbt)
    {
    }

    void OnReadNbt(Entity self, NBTTagCompound nbt)
    {
    }

    /// <summary>
    ///     Replaces the type's declared despawn rule, or <c>null</c> to keep it. A tamed wolf is
    ///     never despawned.
    /// </summary>
    bool? CanDespawn(EntityLiving self) => null;
}
