namespace OmniBlock.Network.Packets;

/// <summary>
///     What is left of Beta 1.7.3's flat byte-ID framing.
///     <para>
///         Four entries, and no room for a fifth to be wanted. Everything that describes the world —
///         blocks, entities, chunks, inventories, chat, movement — travels inside
///         <see cref="OmniMessage" />, keyed by name rather than by number, so adding a message never
///         consumes a slot here and two mods cannot collide over one. See Network/Messages/.
///     </para>
///     <para>
///         These four cannot follow, and for the same reason in every case: they are what establishes
///         the naming. The login exchange runs before either peer knows the other speaks the
///         protocol, and <see cref="MessageRegistrySyncS2C" /> <em>is</em> the table that assigns
///         each message its session ID, so it cannot be identified by an entry in the table it
///         carries. This is the floor, not a migration that has not happened yet.
///     </para>
/// </summary>
public enum PacketId : byte
{
    LoginHello = 1,
    Handshake = 2,

    // 240-254 are reserved for the extensible message layer; only these two are spent.
    MessageRegistrySyncS2C = 240,
    OmniMessage = 241
}
