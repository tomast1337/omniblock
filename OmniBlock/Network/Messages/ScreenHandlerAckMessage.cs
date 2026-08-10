using OmniBlock;

namespace OmniBlock.Network.Messages;

/// <summary>
///     Whether the server agreed with what a click produced. Replaces
///     <c>ScreenHandlerAcknowledgementPacket</c>.
///     <para>
///         Travels both ways: the server answers a click, and the client confirms having applied a
///         correction. The revision is what ties the two to a particular click rather than to the
///         screen as a whole.
///     </para>
/// </summary>
public sealed class ScreenHandlerAckMessage : Message
{
    public sbyte SyncId { get; set; }

    /// <summary>The click's revision, matching <c>ClickSlotMessage.ActionType</c>.</summary>
    public short ActionType { get; set; }

    public bool Accepted { get; set; }

    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "screen_ack");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        SyncId = (sbyte)stream.ReadByte();
        ActionType = stream.ReadShort();
        Accepted = stream.ReadBoolean();
    }

    public override void Write(Stream stream)
    {
        stream.WriteByte((byte)SyncId);
        stream.WriteShort(ActionType);
        stream.WriteBoolean(Accepted);
    }

    public override int Size()
    {
        return
            1
            + 2
            + 1;
    }
}
