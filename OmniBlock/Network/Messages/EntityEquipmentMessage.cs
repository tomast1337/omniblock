using OmniBlock;

namespace OmniBlock.Network.Messages;

/// <summary>
///     What another entity is visibly wearing or holding. Replaces
///     <c>EntityEquipmentUpdateS2CPacket</c>.
///     <para>
///         Not an <c>ItemStack</c> field: this carries an item ID and a damage value with no count,
///         because a rendered slot has no stack size. Encoding it as a stack would put a byte on the
///         wire that means nothing and invite the receiver to believe it.
///     </para>
/// </summary>
public sealed class EntityEquipmentMessage : Message
{
    public override SendPriority Priority => SendPriority.High;

    public int EntityId { get; set; }

    /// <summary>0 is the held item; 1 to 4 are the armour slots.</summary>
    public short Slot { get; set; }

    /// <summary>-1 for an empty slot.</summary>
    public short ItemRawId { get; set; } = -1;

    public short ItemDamage { get; set; }

    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "entity_equipment");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        EntityId = stream.ReadInt();
        Slot = stream.ReadShort();
        ItemRawId = stream.ReadShort();
        ItemDamage = stream.ReadShort();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteShort(Slot);
        stream.WriteShort(ItemRawId);
        stream.WriteShort(ItemDamage);
    }

    public override int Size()
    {
        return
            4
            + 2
            + 2
            + 2;
    }
}
