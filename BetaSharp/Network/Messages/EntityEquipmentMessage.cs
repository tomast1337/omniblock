namespace BetaSharp.Network.Messages;

/// <summary>
///     What another entity is visibly wearing or holding. Replaces
///     <c>EntityEquipmentUpdateS2CPacket</c>.
///     <para>
///         Not an <c>ItemStack</c> field: this carries an item ID and a damage value with no count,
///         because a rendered slot has no stack size. Encoding it as a stack would put a byte on the
///         wire that means nothing and invite the receiver to believe it.
///     </para>
/// </summary>
[WireMessage("betasharp:entity_equipment")]
public sealed partial class EntityEquipmentMessage : Message
{
    public override SendPriority Priority => SendPriority.High;

    [WireField]
    public int EntityId { get; set; }

    /// <summary>0 is the held item; 1 to 4 are the armour slots.</summary>
    [WireField]
    public short Slot { get; set; }

    /// <summary>-1 for an empty slot.</summary>
    [WireField]
    public short ItemRawId { get; set; } = -1;

    [WireField]
    public short ItemDamage { get; set; }
}
