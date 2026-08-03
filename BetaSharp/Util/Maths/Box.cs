using System.Runtime.CompilerServices;
using BetaSharp.Util.Hit;

namespace BetaSharp.Util.Maths;

public struct Box(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
{
    public double MinX { get; set; } = Math.Min(minX, maxX);
    public double MinY { get; set; } = Math.Min(minY, maxY);
    public double MinZ { get; set; } = Math.Min(minZ, maxZ);
    public double MaxX { get; set; } = Math.Max(minX, maxX);
    public double MaxY { get; set; } = Math.Max(minY, maxY);
    public double MaxZ { get; set; } = Math.Max(minZ, maxZ);

    public Box Stretch(double x, double y, double z)
    {
        (double newMinX, double newMaxX) = x < 0 ? (MinX + x, MaxX) : (MinX, MaxX + x);
        (double newMinY, double newMaxY) = y < 0 ? (MinY + y, MaxY) : (MinY, MaxY + y);
        (double newMinZ, double newMaxZ) = z < 0 ? (MinZ + z, MaxZ) : (MinZ, MaxZ + z);

        return new Box(newMinX, newMinY, newMinZ, newMaxX, newMaxY, newMaxZ);
    }

    public Box Expand(double x, double y, double z) =>
        new(MinX - x, MinY - y, MinZ - z, MaxX + x, MaxY + y, MaxZ + z);

    public Box Offset(double x, double y, double z) =>
        new(MinX + x, MinY + y, MinZ + z, MaxX + x, MaxY + y, MaxZ + z);

    public double GetXOffset(in Box other, double offsetX)
    {
        if (other.MaxY <= MinY || other.MinY >= MaxY || other.MaxZ <= MinZ || other.MinZ >= MaxZ)
        {
            return offsetX;
        }

        if (offsetX > 0 && other.MaxX <= MinX)
        {
            return Math.Min(offsetX, MinX - other.MaxX);
        }

        if (offsetX < 0 && other.MinX >= MaxX)
        {
            return Math.Max(offsetX, MaxX - other.MinX);
        }

        return offsetX;
    }

    public double GetYOffset(in Box other, double offsetY)
    {
        if (other.MaxX <= MinX || other.MinX >= MaxX || other.MaxZ <= MinZ || other.MinZ >= MaxZ)
        {
            return offsetY;
        }

        if (offsetY > 0 && other.MaxY <= MinY)
        {
            double diff = MinY - other.MaxY;
            if (diff < offsetY)
            {
                offsetY = diff;
            }
        }
        else if (offsetY < 0 && other.MinY >= MaxY)
        {
            double diff = MaxY - other.MinY;
            if (diff > offsetY)
            {
                offsetY = diff;
            }
        }

        return offsetY;
    }

    public double GetZOffset(in Box other, double offsetZ)
    {
        if (other.MaxX <= MinX || other.MinX >= MaxX || other.MaxY <= MinY || other.MinY >= MaxY)
            return offsetZ;

        if (offsetZ > 0 && other.MaxZ <= MinZ)
        {
            double diff = MinZ - other.MaxZ;
            if (diff < offsetZ)
            {
                offsetZ = diff;
            }
        }
        else if (offsetZ < 0 && other.MinZ >= MaxZ)
        {
            double diff = MaxZ - other.MinZ;
            if (diff > offsetZ)
            {
                offsetZ = diff;
            }
        }

        return offsetZ;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(in Box other) =>
        other.MaxX > MinX && other.MinX < MaxX &&
        other.MaxY > MinY && other.MinY < MaxY &&
        other.MaxZ > MinZ && other.MinZ < MaxZ;

    public Box Translate(double x, double y, double z)
    {
        MinX += x;
        MinY += y;
        MinZ += z;
        MaxX += x;
        MaxY += y;
        MaxZ += z;
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(in Vec3D pos) =>
        pos.X > MinX && pos.X < MaxX &&
        pos.Y > MinY && pos.Y < MaxY &&
        pos.Z > MinZ && pos.Z < MaxZ;

    public double AverageEdgeLength => (MaxX - MinX + (MaxY - MinY) + (MaxZ - MinZ)) / 3.0;

    public Box Contract(double x, double y, double z) =>
        new(MinX + x, MinY + y, MinZ + z, MaxX - x, MaxY - y, MaxZ - z);

    private enum Axis { X, Y, Z}

    public HitResult Raycast(Vec3D start, Vec3D end)
    {
        Vec3D? hitX = GetClosest(start, end, start.GetIntermediateWithXValue(end, MinX), start.GetIntermediateWithXValue(end, MaxX), Axis.X);
        Vec3D? hitY = GetClosest(start, end, start.GetIntermediateWithYValue(end, MinY), start.GetIntermediateWithYValue(end, MaxY), Axis.Y);
        Vec3D? hitZ = GetClosest(start, end, start.GetIntermediateWithZValue(end, MinZ), start.GetIntermediateWithZValue(end, MaxZ), Axis.Z);

        Vec3D? finalHit = null;
        int side = -1;

        UpdateHit(hitX, ref finalHit, ref side, start.GetIntermediateWithXValue(end, MinX) == hitX ? 4 : 5);
        UpdateHit(hitY, ref finalHit, ref side, start.GetIntermediateWithYValue(end, MinY) == hitY ? 0 : 1);
        UpdateHit(hitZ, ref finalHit, ref side, start.GetIntermediateWithZValue(end, MinZ) == hitZ ? 2 : 3);

        return finalHit is null
            ? new HitResult(HitResultType.Miss)
            : new HitResult(0, 0, 0, side, finalHit.Value, HitResultType.Tile);

        void UpdateHit(in Vec3D? candidate, ref Vec3D? current, ref int currentSide, int candidateSide)
        {
            if (candidate is null)
            {
                return;
            }

            if (current is not null && !(start.DistanceTo(candidate.Value) < start.DistanceTo(current.Value)))
            {
                return;
            }

            current = candidate;
            currentSide = candidateSide;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsValid(in Vec3D p, Axis axis) => axis switch
    {
        Axis.X => p.Y >= MinY && p.Y <= MaxY && p.Z >= MinZ && p.Z <= MaxZ,
        Axis.Y => p.X >= MinX && p.X <= MaxX && p.Z >= MinZ && p.Z <= MaxZ,
        Axis.Z => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY,
        _ => false
    };

    private Vec3D? GetClosest(in Vec3D start, in Vec3D end, in Vec3D? a, in Vec3D? b, Axis axis)
    {
        bool aValid = a is not null && IsValid(a.Value, axis);
        bool bValid = b is not null && IsValid(b.Value, axis);

        if (aValid && bValid)
        {
            return start.DistanceTo(a!.Value) < start.DistanceTo(b!.Value) ? a : b;
        }

        return aValid ? a : bValid ? b : null;
    }

    public override string ToString() => $"Box[{MinX:F2}, {MinY:F2}, {MinZ:F2} -> {MaxX:F2}, {MaxY:F2}, {MaxZ:F2}]";
}
