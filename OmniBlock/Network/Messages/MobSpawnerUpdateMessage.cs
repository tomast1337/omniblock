using OmniBlock.Util;

namespace OmniBlock.Network.Messages;

/// <summary>The persisted target that a mob spawner previews and attempts to spawn.</summary>
public sealed class MobSpawnerUpdateMessage : Message
{
    // A ResourceLocation permits two 256-character components plus the separating colon.
    public const int MaxEntityIdBytes = 513;

    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "mob_spawner_update");

    public int X { get; set; }
    public short Y { get; set; }
    public int Z { get; set; }
    public string EntityTypeId { get; set; } = string.Empty;

    public override ResourceLocation Key => Id;

    public override void Read(Stream stream)
    {
        X = stream.ReadInt();
        Y = stream.ReadShort();
        Z = stream.ReadInt();
        EntityTypeId = stream.ReadString(MaxEntityIdBytes);
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(X);
        stream.WriteShort(Y);
        stream.WriteInt(Z);
        stream.WriteString(EntityTypeId);
    }

    public override int Size() =>
        4
        + 2
        + 4
        + 2 + ModifiedUtf8.GetByteCount(EntityTypeId);
}
