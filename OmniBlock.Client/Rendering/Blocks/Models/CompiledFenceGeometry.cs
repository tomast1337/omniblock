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
        var cuboids = CompiledCuboidParts.Build(model, fixedLayers, definitions.Rules.ToArray().Select(rule => rule.Element));
        var parts = new List<Part>();
        foreach (var rule in definitions.Rules)
        {
            var piece = cuboids.Get(rule.Element);
            parts.Add(new Part(rule.RequiredConnection, piece.Bounds, piece.Geometry));
        }
        return new CompiledFenceGeometry(parts.ToArray());
    }
}
