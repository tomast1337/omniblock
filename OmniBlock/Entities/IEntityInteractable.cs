namespace OmniBlock.Entities;

/// <summary>
///     Composable player interaction. Declared on <see cref="Entity" />, not
///     <see cref="EntityLiving" />, because vehicles and dropped items are interacted with too.
/// </summary>
public interface IEntityInteractable
{
    /// <summary>
    ///     Right-click. Returning <c>true</c> means the interaction was handled and the player's
    ///     held item is not used on the entity afterwards.
    /// </summary>
    bool OnInteract(Entity self, EntityPlayer player) => false;

    /// <summary>Walking into the entity: item pickup, a slime's contact damage.</summary>
    void OnPlayerCollision(Entity self, EntityPlayer player)
    {
    }
}
