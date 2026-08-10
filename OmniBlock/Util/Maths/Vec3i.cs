namespace OmniBlock.Util.Maths;

public record struct Vec3I(int X, int Y, int Z) : IComparable<Vec3I>
{
    public static readonly Vec3I Zero = new(0, 0, 0);

    public int CompareTo(Vec3I other)
    {
        if (Y != other.Y)
        {
            return Y.CompareTo(other.Y);
        }

        return Z != other.Z ? Z.CompareTo(other.Z) : X.CompareTo(other.X);
    }

    public int SquaredDistanceTo(Vec3I other) => Y == other.Y ? Z == other.Z ? X - other.X : Z - other.Z : Y - other.Y;

    public static explicit operator Vec3D(Vec3I v) => new(v.X, v.Y, v.Z);
}
