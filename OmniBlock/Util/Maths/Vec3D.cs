namespace OmniBlock.Util.Maths;

public record struct Vec3D
{
    private const double Tolerance = 1.0E-7F;

    public static readonly Vec3D Zero = new(0.0D, 0.0D, 0.0D);

    public double X;
    public double Y;
    public double Z;

    public Vec3D(double x, double y, double z)
    {
        if (Math.Abs(x - -0.0D) < Tolerance)
        {
            x = 0.0D;
        }

        if (Math.Abs(y - -0.0D) < Tolerance)
        {
            y = 0.0D;
        }

        if (Math.Abs(z - -0.0D) < Tolerance)
        {
            z = 0.0D;
        }

        X = x;
        Y = y;
        Z = z;
    }

    public double SquareDistanceTo(Vec3D other)
    {
        var dx = other.X - X;
        var dy = other.Y - Y;
        var dz = other.Z - Z;
        return dx * dx + dy * dy + dz * dz;
    }

    public double DistanceTo(Vec3D other) => Math.Sqrt(SquareDistanceTo(other));

    public double SquareDistance2DTo(Vec3D other)
    {
        var dx = other.X - X;
        var dz = other.Z - Z;
        return dx * dx + dz * dz;
    }

    public double Magnitude() => DistanceTo(Zero);

    public Vec3D Normalize()
    {
        var mag = Magnitude();
        return mag < 1.0E-4D ? Zero : this / mag;
    }

    public Vec3D CrossProduct(Vec3D other) => new(Y * other.Z - Z * other.Y, Z * other.X - X * other.Z, X * other.Y - Y * other.X);

    public Vec3D? GetIntermediateWithXValue(Vec3D other, double xValue)
    {
        var deltaX = other.X - X;
        var deltaY = other.Y - Y;
        var deltaZ = other.Z - Z;
        if (deltaX * deltaX < 1.0E-7F)
        {
            return null;
        }

        var progress = (xValue - X) / deltaX;
        return progress is >= 0.0D and <= 1.0D ? new Vec3D(X + deltaX * progress, Y + deltaY * progress, Z + deltaZ * progress) : null;
    }

    public Vec3D? GetIntermediateWithYValue(Vec3D other, double yValue)
    {
        var deltaX = other.X - X;
        var deltaY = other.Y - Y;
        var deltaZ = other.Z - Z;
        if (deltaY * deltaY < 1.0E-7F)
        {
            return null;
        }

        var progress = (yValue - Y) / deltaY;
        return progress is >= 0.0D and <= 1.0D ? new Vec3D(X + deltaX * progress, Y + deltaY * progress, Z + deltaZ * progress) : null;
    }

    public Vec3D? GetIntermediateWithZValue(Vec3D other, double zValue)
    {
        var deltaX = other.X - X;
        var deltaY = other.Y - Y;
        var deltaZ = other.Z - Z;
        if (deltaZ * deltaZ < 1.0E-7F)
        {
            return null;
        }

        var progress = (zValue - Z) / deltaZ;
        return progress is >= 0.0D and <= 1.0D ? new Vec3D(X + deltaX * progress, Y + deltaY * progress, Z + deltaZ * progress) : null;
    }

    public void RotateAroundX(float angleRadians)
    {
        var cosAngle = MathHelper.Cos(angleRadians);
        var sinAngle = MathHelper.Sin(angleRadians);

        var rotatedY = Y * cosAngle + Z * sinAngle;
        var rotatedZ = Z * cosAngle - Y * sinAngle;

        Y = rotatedY;
        Z = rotatedZ;
    }

    public void RotateAroundY(float angleRadians)
    {
        var cosAngle = MathHelper.Cos(angleRadians);
        var sinAngle = MathHelper.Sin(angleRadians);

        var rotatedX = X * cosAngle + Z * sinAngle;
        var rotatedZ = Z * cosAngle - X * sinAngle;

        X = rotatedX;
        Z = rotatedZ;
    }

    public override string ToString() => $"({X}, {Y}, {Z})";

    public string ToString(string format) => $"({X.ToString(format)}, {Y.ToString(format)}, {Z.ToString(format)})";

    public static Vec3D operator +(Vec3D a, Vec3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vec3D operator -(Vec3D a, Vec3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vec3D operator *(Vec3D a, Vec3D b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

    public static Vec3D operator /(Vec3D a, Vec3D b) => new(a.X / b.X, a.Y / b.Y, a.Z / b.Z);

    public static Vec3D operator *(double a, Vec3D b) => new(a * b.X, a * b.Y, a * b.Z);

    public static Vec3D operator /(Vec3D a, double b) => new(a.X / b, a.Y / b, a.Z / b);
}
