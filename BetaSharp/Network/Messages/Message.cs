namespace BetaSharp.Network.Messages;

/// <summary>
///     A message in the extensible protocol layer, identified by a <see cref="ResourceLocation" />
///     rather than by a byte from a fixed enum.
///     <para>
///         This is what <c>PacketId : byte</c> cannot be. That enum has 256 slots and no owner, so a
///         mod adding a packet has to pick a number and hope nothing else wants it. A message is
///         named — <c>omniblock:time_sync_request</c>, <c>mymod:spell_cast</c> — and the integer it
///         travels as is assigned per session by <see cref="MessageRegistry" />. There is no ID to
///         collide over and load order does not change the wire encoding.
///     </para>
///     <para>
///         Messages travel inside <c>OmniMessagePacket</c>, which length-prefixes the payload. That
///         is the second thing the legacy framing cannot do: an unknown byte ID leaves the reader
///         with no idea where the next packet starts, so the connection dies. An unknown message is
///         skipped.
///     </para>
/// </summary>
public abstract class Message
{
    /// <summary>
    ///     Stable identity, and the sort key for ID assignment. Must not vary between runs or
    ///     between client and server, since both sides derive the same ordering from it.
    /// </summary>
    public abstract ResourceLocation Key { get; }

    /// <summary>
    ///     Schema version for this message type. Bumped when the payload shape changes.
    ///     <para>
    ///         Per-type rather than one global protocol number, so a server one version ahead on a
    ///         single message degrades that one feature instead of refusing the connection. A global
    ///         version forces lockstep upgrades across an entire mod ecosystem.
    ///     </para>
    /// </summary>
    public virtual int SchemaVersion => 1;

    public abstract void Read(Stream stream);

    public abstract void Write(Stream stream);

    /// <summary>Serialised byte count, excluding the envelope's ID and length prefix.</summary>
    public abstract int Size();
}
