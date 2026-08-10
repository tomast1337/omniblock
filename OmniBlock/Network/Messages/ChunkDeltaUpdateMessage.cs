namespace OmniBlock.Network.Messages;

/// <summary>
///     Carries a run of block changes within one chunk. The generator has no encoding for
///     parallel arrays keyed by a single count, so this one stays hand-written.
/// </summary>
/// <remarks>
///     Carries no light, for the reason <see cref="BlockUpdateMessage" /> gives.
/// </remarks>
public sealed class ChunkDeltaUpdateMessage : Message
{
    /// <summary>One chunk column full of blocks.</summary>
    public const int MaxCount = 16 * 128 * 16;

    public static readonly ResourceLocation Id = new(Namespace.OmniBlock, "chunk_delta_update");

    public override ResourceLocation Key => Id;

    public int X { get; set; }
    public int Z { get; set; }

    /// <summary>
    ///     Packed position: (x << 12) | (z << 8) | y.
    /// </summary>
    public short[] Positions { get; set; } = [];

    public byte[] BlockRawIds { get; set; } = [];
    public byte[] BlockMetadata { get; set; } = [];

    public override void Read(Stream stream)
    {
        X = stream.ReadInt();
        Z = stream.ReadInt();
        int count = stream.ReadShort() & 0xffff;
        if (count is < 0 or > MaxCount)
        {
            throw new InvalidDataException(
                $"Chunk delta declares {count} entries; the limit is {MaxCount}.");
        }

        Positions = new short[count];
        BlockRawIds = new byte[count];
        BlockMetadata = new byte[count];

        for (int i = 0; i < count; i++)
        {
            Positions[i] = stream.ReadShort();
        }

        stream.ReadExactly(BlockRawIds);
        stream.ReadExactly(BlockMetadata);
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(X);
        stream.WriteInt(Z);
        stream.WriteShort((short)Positions.Length);

        foreach (short t in Positions)
        {
            stream.WriteShort(t);
        }

        stream.Write(BlockRawIds);
        stream.Write(BlockMetadata);
    }

    public override int Size() => sizeof(int) * 2 + sizeof(short) + Positions.Length * (sizeof(short) + 2);
}
