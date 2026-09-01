namespace OmniBlock.Blocks;

public enum Side : byte
{
    Down = 0,
    Up = 1,
    North = 2,
    South = 3,
    West = 4,
    East = 5
}

public static class SideExtensions
{
    public static Side ToSide(this int v)
    {
        return ((Side)v).IsValidSide() ? (Side)v : throw new ArgumentException("Invalid side");
    }

    extension(Side s)
    {
        public int ToInt()
        {
            return (int)s;
        }

        public Side OppositeFace()
        {
            return s switch
            {
                Side.Down => Side.Up,
                Side.Up => Side.Down,
                Side.North => Side.South,
                Side.South => Side.North,
                Side.West => Side.East,
                Side.East => Side.West,
                _ => throw new ArgumentOutOfRangeException(nameof(s), s, null)
            };
        }

        public bool IsValidSide()
        {
            return (byte)s <= 5;
        }
    }
}