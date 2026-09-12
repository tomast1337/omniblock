using System.Numerics;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>Owned CPU geometry: no gameplay entity, shared pose, static geometry reservation or renderer state.</summary>
internal static class CowImpostorGeometry
{
    public static EntityImpostorVertex[] Build() => BuildPoses()[0];

    /// <summary>One idle pose and four full-stride samples matching <see cref="ModelCow"/>.</summary>
    public static EntityImpostorVertex[][] BuildPoses() => EntityImpostorGeometry.BuildPoses("cow");
}

/// <summary>Version 2 orthographic capture basis and bounded cow pose atlas.</summary>
internal static class EntityImpostorLayout
{
    public const int Version = 2;
    public const int Views = 26, Poses = 5, Captures = Views * Poses;
    public const int Tile = 64, Padding = 2, Cell = Tile + Padding * 2, Columns = 6, RowsPerPose = 5;
    public const int Width = Columns * Cell, Height = RowsPerPose * Poses * Cell;
    public static (Vector3 Right, Vector3 Up) Basis(Vector3 direction)
    {
        direction = Vector3.Normalize(direction);
        var reference = Math.Abs(direction.Y) > .999f ? -Vector3.UnitZ : Vector3.UnitY;
        var right = Vector3.Normalize(Vector3.Cross(reference, direction));
        return (right, Vector3.Normalize(Vector3.Cross(direction, right)));
    }
    public static int AtlasHeight(int layers) => Height * layers;
    public static int CapturesFor(int layers) => Captures * layers;
    public static Vector4 UV(int view, int pose = 0, int layers = 1)
    {
        if (view is < 0 or >= Views) throw new ArgumentOutOfRangeException(nameof(view));
        if (pose is < 0 or >= Poses) throw new ArgumentOutOfRangeException(nameof(pose));
        if (layers is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(layers));
        return new Vector4((view % Columns * Cell + Padding) / (float)Width,
            ((pose * RowsPerPose + view / Columns) * Cell + Padding) / (float)AtlasHeight(layers),
            Tile / (float)Width, Tile / (float)AtlasHeight(layers));
    }
}
