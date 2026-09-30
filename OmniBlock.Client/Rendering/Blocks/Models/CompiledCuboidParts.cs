using OmniBlock.Blocks;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>Validated named cuboids shared by restricted fence and stair multipart bindings.</summary>
internal sealed class CompiledCuboidParts
{
    internal readonly record struct Part(Box Bounds, CompiledCuboidGeometry Geometry);
    private readonly Dictionary<string, Part> _parts;

    private CompiledCuboidParts(Dictionary<string, Part> parts) => _parts = parts;
    internal Part Get(string name) => _parts[name];

    internal static CompiledCuboidParts Build(CompiledBlockModel model,
        IReadOnlyDictionary<RenderResourceId, int> fixedLayers, IEnumerable<string> expectedNames)
    {
        if (!model.AmbientOcclusion) throw Invalid("ambient occlusion must remain enabled");
        var groups = model.Quads.ToArray().GroupBy(quad => quad.Part, StringComparer.Ordinal)
            .ToDictionary(group => group.Key ?? "", group => group.ToArray(), StringComparer.Ordinal);
        var names = new HashSet<string>(expectedNames, StringComparer.Ordinal);
        if (groups.Keys.Any(name => !names.Contains(name)) || names.Any(name => !groups.ContainsKey(name)))
            throw Invalid($"missing named element or unreferenced element: expected [{string.Join(", ", names.Order(StringComparer.Ordinal))}]");
        var parts = new Dictionary<string, Part>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var quads = groups[name];
            if (quads.Length != 6 || quads.Select(quad => quad.Direction).Distinct().Count() != 6)
                throw Invalid($"element '{name}' must contain exactly six cuboid faces");
            var vertices = quads.SelectMany(q => new[] { q.A.Position, q.B.Position, q.C.Position, q.D.Position }).ToArray();
            var bounds = new Box(vertices.Min(v => v.X), vertices.Min(v => v.Y), vertices.Min(v => v.Z),
                vertices.Max(v => v.X), vertices.Max(v => v.Y), vertices.Max(v => v.Z));
            if (bounds.MinX < 0 || bounds.MinY < 0 || bounds.MinZ < 0 ||
                bounds.MaxX > 1 || bounds.MaxY > 1 || bounds.MaxZ > 1 ||
                bounds.MinX == bounds.MaxX || bounds.MinY == bounds.MaxY || bounds.MinZ == bounds.MaxZ)
                throw Invalid($"element '{name}' has invalid bounds");
            foreach (var quad in quads)
            {
                if (!fixedLayers.TryGetValue(quad.Texture, out var layer) || layer != quad.ArrayLayer)
                    throw Invalid($"element '{name}' uses unsupported texture '{quad.Texture}'");
                if (!quad.Shade || quad.TintIndex != -1 || quad.CullFace is { } cull && cull != quad.Direction ||
                    !OnFace(quad, bounds))
                    throw Invalid($"element '{name}' has unsupported face shape, tint, shade or culling");
            }
            parts.Add(name, new Part(bounds, new CompiledCuboidGeometry(new CompiledBlockModel(model.Id, true, quads), true)));
        }
        return new CompiledCuboidParts(parts);

        InvalidDataException Invalid(string reason) => new($"Block model '{model.Id}': {reason}.");
    }

    private static bool OnFace(CompiledBlockQuad quad, Box bounds)
    {
        var axis = quad.Direction switch
        {
            Side.Down or Side.Up => 1,
            Side.North or Side.South => 2,
            _ => 0
        };
        var plane = quad.Direction switch
        {
            Side.Down => bounds.MinY, Side.Up => bounds.MaxY,
            Side.North => bounds.MinZ, Side.South => bounds.MaxZ,
            Side.West => bounds.MinX, _ => bounds.MaxX
        };
        var corners = new[] { quad.A.Position, quad.B.Position, quad.C.Position, quad.D.Position };
        return corners.Distinct().Count() == 4 && corners.All(v =>
            Math.Abs((axis == 0 ? v.X : axis == 1 ? v.Y : v.Z) - plane) < 0.00001 &&
            (v.X == bounds.MinX || v.X == bounds.MaxX) &&
            (v.Y == bounds.MinY || v.Y == bounds.MaxY) &&
            (v.Z == bounds.MinZ || v.Z == bounds.MaxZ));
    }
}
