using OmniBlock;

namespace OmniBlock.Network.Messages;

/// <summary>
///     The newest snapshot this peer has actually applied, so the server knows which state its next
///     delta may be measured against.
///     <para>
///         Four bytes a tick, which buys the whole delta scheme: without it the server can only
///         measure against the snapshot it sent last,
///         and that is only sound on a channel that guarantees the client received it.
///     </para>
///     <para>
///         <b>Applied, not received.</b> A snapshot whose baseline the client could not reconstruct
///         is dropped, and acknowledging it would leave the server encoding against a state the
///         client never reached — the one failure mode of delta compression that produces plausible
///         wrong positions instead of a visible fault.
///     </para>
/// </summary>
public sealed class SnapshotAckMessage : Message
{
    /// <summary>
    ///     An acknowledgement that arrives late costs a snapshot's worth of redundant delta, so it
    ///     travels with the snapshots it is answering rather than behind bulk traffic.
    /// </summary>
    public override SendPriority Priority => SendPriority.High;

    /// <summary>Zero means nothing has been applied yet, and the server must send an absolute snapshot.</summary>
    public uint Sequence { get; set; }

    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "snapshot_ack");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        Sequence = (uint)stream.ReadVarInt();
    }

    public override void Write(Stream stream)
    {
        stream.WriteVarInt((int)Sequence);
    }

    public override int Size()
    {
        return
            StreamExtensions.VarIntSize((int)Sequence);
    }
}
