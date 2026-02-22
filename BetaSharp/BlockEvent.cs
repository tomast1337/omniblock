namespace BetaSharp;

public class BlockEvent(int x, int y, int z, int blockId)
    : IComparable
{
    private static long s_nextTickEntryId;
    public readonly int X = x;
    public readonly int Y = y;
    public readonly int Z = z;
    public readonly int BlockId = blockId;
    public long Ticks;
    private readonly long _tickEntryId = s_nextTickEntryId++;

    public override bool Equals(object other)
    {
        if (other is not BlockEvent blockEvent)
        {
            return false;
        }

        return X == blockEvent.X && Y == blockEvent.Y && Z == blockEvent.Z && BlockId == blockEvent.BlockId;
    }

    public override int GetHashCode()
    {
        return (X * 128 * 1024 + Z * 128 + Y) * 256 + BlockId;
    }

    public int CompareTo(BlockEvent other)
    {
        return Ticks < other.Ticks ? -1 : (Ticks > other.Ticks ? 1 : (_tickEntryId < other._tickEntryId ? -1 : (_tickEntryId > other._tickEntryId ? 1 : 0)));
    }

    public int CompareTo(object? other)
    {
        return CompareTo((BlockEvent)other!);
    }
}
