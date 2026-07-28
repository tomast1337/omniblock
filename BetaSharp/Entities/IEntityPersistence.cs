using BetaSharp.NBT;

namespace BetaSharp.Entities;

/// <summary>
///     Composable NBT persistence, for state that needs more than a declared synced property can
///     express — packed bit fields, values whose setter has side effects, plain unsynced fields.
///     <para>
///         A synced property that maps one-to-one onto an NBT key does not need this: give it an
///         <c>Nbt</c> key in the entity's JSON and it round-trips automatically.
///     </para>
/// </summary>
public interface IEntityPersistence
{
    void OnWriteNbt(Entity self, NBTTagCompound nbt) { }
    void OnReadNbt(Entity self, NBTTagCompound nbt) { }

    /// <summary>
    ///     Replaces the type's declared despawn rule, or <c>null</c> to keep it. Whether a mob is
    ///     kept is persistence in the plainest sense: a tamed wolf is somebody's, so the world holds
    ///     on to it.
    /// </summary>
    bool? CanDespawn(EntityLiving self) => null;
}
