using OmniBlock;
using OmniBlock.Util;

namespace OmniBlock.Network.Messages;

/// <summary>
///     Spawns another player. Replaces <c>PlayerSpawnS2CPacket</c>.
///     <para>
///         The packet declared 28 bytes for a payload that is 22 plus twice the name's length — 54
///         at the sixteen-character limit, so it was wrong on every spawn it ever sent.
///     </para>
/// </summary>
public sealed class PlayerSpawnMessage : Message
{
    /// <summary>The account name's limit, and the bound the reader applies before allocating.</summary>
    public const int MaxNameBytes = 16;

    public override SendPriority Priority => SendPriority.High;

    public int EntityId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Fixed point in sixteenths of a block.</summary>
    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public sbyte Yaw { get; set; }

    public sbyte Pitch { get; set; }

    /// <summary>The item in hand, for rendering only. 0 is an empty hand.</summary>
    public short CurrentItem { get; set; }

    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "player_spawn");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        EntityId = stream.ReadInt();
        Name = stream.ReadString(16);
        X = stream.ReadInt();
        Y = stream.ReadInt();
        Z = stream.ReadInt();
        Yaw = (sbyte)stream.ReadByte();
        Pitch = (sbyte)stream.ReadByte();
        CurrentItem = stream.ReadShort();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteString(Name);
        stream.WriteInt(X);
        stream.WriteInt(Y);
        stream.WriteInt(Z);
        stream.WriteByte((byte)Yaw);
        stream.WriteByte((byte)Pitch);
        stream.WriteShort(CurrentItem);
    }

    public override int Size()
    {
        return
            4
            + (2 + ModifiedUtf8.GetByteCount(Name))
            + 4
            + 4
            + 4
            + 1
            + 1
            + 2;
    }
}
