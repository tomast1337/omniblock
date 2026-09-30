using System.Numerics;
using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>Immutable named model elements with build-time connection predicates.</summary>
internal sealed class CompiledFenceGeometry
{
    private readonly Part[] _parts;
    private readonly record struct Part(int RequiredConnection, Box Bounds, CompiledCuboidGeometry Geometry);

    private CompiledFenceGeometry(Part[] parts) => _parts = parts;

    internal bool Draw(Block block, in BlockPos pos, ref BlockRenderContext context)
    {
        var mask = FenceShape.ConnectionMask(context.BlockReader, block.Id, pos.X, pos.Y, pos.Z);
        foreach (var part in _parts)
        {
            if (part.RequiredConnection != 0 && (mask & part.RequiredConnection) == 0) continue;
            var selected = context with { OverrideBounds = part.Bounds, CompiledCuboid = part.Geometry };
            selected.DrawBlock(block, pos);
        }
        return true;
    }

    internal static CompiledFenceGeometry Build(CompiledBlockModel model, FencePartDefinitions definitions,
        IReadOnlyDictionary<RenderResourceId, int> fixedLayers)
    {
        if (!model.AmbientOcclusion) throw Invalid("ambient occlusion must remain enabled");
        var grouped = model.Quads.ToArray().GroupBy(quad => quad.Part, StringComparer.Ordinal)
            .ToDictionary(group => group.Key ?? "", group => group.ToArray(), StringComparer.Ordinal);
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var parts = new List<Part>();
        foreach (var rule in definitions.Rules)
        {
            if (!grouped.TryGetValue(rule.Element, out var quads))
                throw Invalid($"missing named element '{rule.Element}'");
            selected.Add(rule.Element);
            if (quads.Length != 6 || quads.Select(quad => quad.Direction).Distinct().Count() != 6)
                throw Invalid($"element '{rule.Element}' must contain exactly six cuboid faces");
            var vertices = quads.SelectMany(q => new[] { q.A.Position, q.B.Position, q.C.Position, q.D.Position }).ToArray();
            var bounds = new Box(vertices.Min(v => v.X), vertices.Min(v => v.Y), vertices.Min(v => v.Z),
                vertices.Max(v => v.X), vertices.Max(v => v.Y), vertices.Max(v => v.Z));
            if (bounds.MinX < 0 || bounds.MinY < 0 || bounds.MinZ < 0 ||
                bounds.MaxX > 1 || bounds.MaxY > 1 || bounds.MaxZ > 1 ||
                bounds.MinX == bounds.MaxX || bounds.MinY == bounds.MaxY || bounds.MinZ == bounds.MaxZ)
                throw Invalid($"element '{rule.Element}' has invalid bounds");
            foreach (var quad in quads)
            {
                if (!fixedLayers.TryGetValue(quad.Texture, out var layer) || layer != quad.ArrayLayer)
                    throw Invalid($"element '{rule.Element}' uses unsupported texture '{quad.Texture}'");
                if (!quad.Shade || quad.TintIndex != -1 || quad.CullFace is { } cull && cull != quad.Direction ||
                    !OnFace(quad, bounds))
                    throw Invalid($"element '{rule.Element}' has unsupported face shape, tint, shade or culling");
            }
            parts.Add(new Part(rule.RequiredConnection, bounds,
                new CompiledCuboidGeometry(new CompiledBlockModel(model.Id, true, quads), true)));
        }
        if (grouped.Keys.Any(name => !selected.Contains(name)))
            throw Invalid("model contains an unreferenced or unnamed element");
        return new CompiledFenceGeometry(parts.ToArray());

        InvalidDataException Invalid(string reason) => new($"Fence model '{model.Id}': {reason}.");
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
        return new[] { quad.A.Position, quad.B.Position, quad.C.Position, quad.D.Position }
            .All(v => Math.Abs((axis == 0 ? v.X : axis == 1 ? v.Y : v.Z) - plane) < 0.00001);
    }
}
