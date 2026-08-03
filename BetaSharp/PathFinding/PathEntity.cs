using BetaSharp.Entities;
using BetaSharp.Util.Maths;

namespace BetaSharp.PathFinding;

internal class PathEntity(PathPoint[] points)
{
    private int _pathIndex;
    public int PathLength { get; } = points.Length;

    public bool IsFinished => _pathIndex >= points.Length;

    public void IncrementPathIndex() => _pathIndex++;

    public PathPoint? GetFinalPoint() => PathLength > 0 ? points[PathLength - 1] : null;

    public Vec3D GetPosition(Entity entity)
    {
        PathPoint currentPoint = points[_pathIndex];

        double x = currentPoint.X + (int)(entity.Width + 1.0f) * 0.5;
        double y = currentPoint.Y;
        double z = currentPoint.Z + (int)(entity.Width + 1.0f) * 0.5;

        return new Vec3D(x, y, z);
    }
}
