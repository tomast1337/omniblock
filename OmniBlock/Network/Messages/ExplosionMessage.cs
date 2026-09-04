using OmniBlock.Util.Maths;

namespace OmniBlock.Network.Messages;

/// <summary>
///     Carries an explosion event and the blocks it destroyed. Block positions are sent
///     as relative sbyte offsets from the explosion centre, so the generator's flat field
///     model cannot express this — it needs a hand-written loop over the record count.
/// </summary>
public sealed class ExplosionMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.OmniBlock, "explosion");

    public override ResourceLocation Key => Id;

    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public float Radius { get; set; }

    /// <summary>Block positions relative to the explosion centre.</summary>
    public List<BlockPos> DestroyedBlocks { get; } = [];

    public override void Read(Stream stream)
    {
        X = stream.ReadDouble();
        Y = stream.ReadDouble();
        Z = stream.ReadDouble();
        Radius = stream.ReadFloat();

        var count = stream.ReadInt();
        DestroyedBlocks.Clear();
        DestroyedBlocks.Capacity = count;

        var originX = (int)X;
        var originY = (int)Y;
        var originZ = (int)Z;

        for (var i = 0; i < count; i++)
        {
            int xOffset = (sbyte)stream.ReadByte();
            int yOffset = (sbyte)stream.ReadByte();
            int zOffset = (sbyte)stream.ReadByte();

            DestroyedBlocks.Add(new BlockPos(originX + xOffset, originY + yOffset, originZ + zOffset));
        }
    }

    public override void Write(Stream stream)
    {
        stream.WriteDouble(X);
        stream.WriteDouble(Y);
        stream.WriteDouble(Z);
        stream.WriteFloat(Radius);
        stream.WriteInt(DestroyedBlocks.Count);

        var originX = (int)X;
        var originY = (int)Y;
        var originZ = (int)Z;

        foreach (var pos in DestroyedBlocks)
        {
            stream.WriteByte((byte)(pos.X - originX));
            stream.WriteByte((byte)(pos.Y - originY));
            stream.WriteByte((byte)(pos.Z - originZ));
        }
    }

    public override int Size() => sizeof(double) * 3 + sizeof(float) + sizeof(int) + DestroyedBlocks.Count * 3;
}
