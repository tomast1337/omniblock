namespace OmniBlock.Entities;

/// <summary>
///     Composable capability for a mob's death drops. No default implementation: a mob either
///     declares what it drops or leaves the slot <c>null</c> and drops nothing.
/// </summary>
public interface IEntityLootBehavior
{
    /// <summary>
    ///     Called server-side when the mob dies. <paramref name="killer" /> is whatever dealt the
    ///     killing blow, or <c>null</c> for environmental deaths. The creeper's bonus record drop is
    ///     the only vanilla case that reads it.
    /// </summary>
    void DropLoot(EntityLiving self, Entity? killer);
}
