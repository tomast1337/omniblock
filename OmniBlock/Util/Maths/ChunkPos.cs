namespace OmniBlock.Util.Maths;

public readonly record struct ChunkPos(int X, int Z)
{
    public readonly int X = X;
    public readonly int Z = Z;

    public static int GetHashCode(int x, int z) => (x < 0 ? int.MinValue : 0) | ((x & short.MaxValue) << 16) | (z < 0 ? -short.MinValue : 0) | (z & short.MaxValue);

    public override int GetHashCode() => GetHashCode(X, Z);
}
