namespace BetaSharp.Util.Maths;

public readonly record struct BlockPos(int X, int Y, int Z)
{
    public readonly int X = X;
    public readonly int Y = Y;
    public readonly int Z = Z;
}
