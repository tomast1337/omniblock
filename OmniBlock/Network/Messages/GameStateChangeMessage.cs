namespace OmniBlock.Network.Messages;

public class GameStateChangeMessage : Message
{
    /// <summary>
    ///     Human-readable reason strings indexed by reason code, for the three reasons
    ///     Beta 1.7.3 knows. A null entry means the reason has no associated message.
    /// </summary>
    public static readonly string?[] Reasons = ["tile.bed.notValid", null, null];

    public static readonly ResourceLocation Id = new(Namespace.Get("beta"), "game_state_change");

    public sbyte Reason { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream) => Reason = (sbyte)stream.ReadByte();

    public override void Write(Stream stream) => stream.WriteByte((byte)Reason);

    public override int Size() => 1;
}
