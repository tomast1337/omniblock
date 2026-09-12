using System.Numerics;
using System.Runtime.InteropServices;
using OmniBlock.Client.Rendering.Entities.Models;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>Owned CPU geometry: no gameplay entity, shared pose, static geometry reservation or renderer state.</summary>
internal static class CowImpostorGeometry
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Vertex(Vector3 Position, Vector2 UV, Vector3 Normal);

    public static Vertex[] Build()
    {
        var document = BbModelLoader.LoadCached("cow");
        List<Vertex> vertices = [];
        foreach (var entry in document.Outliner)
        {
            var group = document.Groups.FirstOrDefault(g => g.Uuid == entry.Uuid && g.Export);
            var element = document.Elements.FirstOrDefault(e => e.Uuid == entry.Children.FirstOrDefault() && e.Export);
            if (group == null || element?.Type != "cube") continue;
            var g = BbModelModelBuilder.ConvertElement(group, element, 0);
            var part = new ModelPart(g.UvU, g.UvV, reserveStaticGeometry: false) { Mirror = g.Mirror };
            part.AddBox(g.BoxX, g.BoxY, g.BoxZ, g.SizeX, g.SizeY, g.SizeZ, g.Inflate);
            var rotation = g.Name is "body" or "udders" ? Matrix4x4.CreateRotationX(MathF.PI / 2) : Matrix4x4.Identity;
            // LivingEntityRenderer at body yaw 0: rotate Y 180, scale (-1,-1,1),
            // translate root down 24/16 + 1/128, then bone pivot and model scale.
            var transform = rotation * Matrix4x4.CreateTranslation(g.PivotX, g.PivotY, g.PivotZ) *
                Matrix4x4.CreateScale(1 / 16f) * Matrix4x4.CreateTranslation(0, -1.5f - 1 / 128f, 0) *
                Matrix4x4.CreateScale(1, -1, -1);
            foreach (var v in part.GetBakedVertices())
                vertices.Add(new Vertex(Vector3.Transform(new Vector3(v.Position.X, v.Position.Y, v.Position.Z), transform), new Vector2(v.U, v.V),
                    Vector3.Normalize(Vector3.TransformNormal(new Vector3(v.Normal.X, v.Normal.Y, v.Normal.Z), transform))));
        }
        if (vertices.Count == 0 || vertices.Count > 65536) throw new InvalidDataException("Invalid cow capture geometry.");
        return vertices.ToArray();
    }
}

/// <summary>Version 1 orthographic capture basis. Poles have a fixed tangent, never a zero cross product.</summary>
internal static class EntityImpostorLayout
{
    public const int Version = 1;
    public const int Views = 26, Tile = 64, Padding = 2, Cell = Tile + Padding * 2, Columns = 6, Rows = 5;
    public const int Width = Columns * Cell, Height = Rows * Cell;
    public static (Vector3 Right, Vector3 Up) Basis(Vector3 direction)
    {
        direction = Vector3.Normalize(direction);
        var reference = Math.Abs(direction.Y) > .999f ? -Vector3.UnitZ : Vector3.UnitY;
        var right = Vector3.Normalize(Vector3.Cross(reference, direction));
        return (right, Vector3.Normalize(Vector3.Cross(direction, right)));
    }
    public static Vector4 UV(int view)
    {
        if (view is < 0 or >= Views) throw new ArgumentOutOfRangeException(nameof(view));
        return new Vector4((view % Columns * Cell + Padding) / (float)Width,
            (view / Columns * Cell + Padding) / (float)Height, Tile / (float)Width, Tile / (float)Height);
    }
}
