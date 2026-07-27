namespace BetaSharp.Entities;

/// <summary>
///     Composable capability for a mob's death drops, replacing the old
///     <c>DropFewItems()</c> / <c>DropItem</c> override pair.
///     <para>
///         Unlike the other three slots, this one has no default implementation: every mob either
///         has a concrete answer for what it drops or leaves the slot <c>null</c> to drop nothing.
///     </para>
/// </summary>
public interface IEntityLootBehavior
{
    /// <summary>
    ///     Called server-side when the mob dies. <paramref name="killer" /> is whatever dealt the
    ///     killing blow, or <c>null</c> for environmental deaths — Creeper's bonus record drop is the
    ///     only vanilla case that reads it.
    /// </summary>
    void DropLoot(EntityLiving self, Entity? killer);
}
