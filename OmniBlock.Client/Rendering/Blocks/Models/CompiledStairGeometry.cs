using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>Immutable straight-stair visual parts selected by the existing four-facing/two-half metadata.</summary>
internal sealed class CompiledStairGeometry
{
    private readonly (CompiledCuboidParts.Part Base, CompiledCuboidParts.Part Step)[] _states;

    private CompiledStairGeometry((CompiledCuboidParts.Part, CompiledCuboidParts.Part)[] states) => _states = states;

    internal bool Draw(Block block, in BlockPos pos, ref BlockRenderContext context, int metadata)
    {
        var (basePart, stepPart) = _states[metadata & 7];
        var baseContext = context with { OverrideBounds = basePart.Bounds, CompiledCuboid = basePart.Geometry };
        var stepContext = context with { OverrideBounds = stepPart.Bounds, CompiledCuboid = stepPart.Geometry };
        return baseContext.DrawBlock(block, pos) | stepContext.DrawBlock(block, pos);
    }

    internal static CompiledStairGeometry Build(CompiledBlockModel model,
        IReadOnlyDictionary<RenderResourceId, int> fixedLayers)
    {
        var names = new[] { "base_lower", "base_upper", "step_0", "step_1", "step_2", "step_3", "step_4", "step_5", "step_6", "step_7" };
        var parts = CompiledCuboidParts.Build(model, fixedLayers, names);
        var states = new (CompiledCuboidParts.Part, CompiledCuboidParts.Part)[8];
        for (var meta = 0; meta < states.Length; meta++)
        {
            var basePart = parts.Get(meta < 4 ? "base_lower" : "base_upper");
            var stepPart = parts.Get("step_" + meta);
            var expected = StairShape.GetBounds(meta);
            if (!SameBounds(basePart.Bounds, expected.Base) || !SameBounds(stepPart.Bounds, expected.Step))
                throw new InvalidDataException($"Stair model '{model.Id}' state {meta}: visual bounds must match the existing collision shape.");
            states[meta] = (basePart, stepPart);
        }
        return new CompiledStairGeometry(states);
    }

    private static bool SameBounds(Box left, Box right) =>
        left.MinX == right.MinX && left.MinY == right.MinY && left.MinZ == right.MinZ &&
        left.MaxX == right.MaxX && left.MaxY == right.MaxY && left.MaxZ == right.MaxZ;
}
