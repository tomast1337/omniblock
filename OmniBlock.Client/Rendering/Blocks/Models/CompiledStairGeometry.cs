using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>Immutable stair parts selected from saved facing/half metadata and captured neighbors.</summary>
internal sealed class CompiledStairGeometry
{
    private readonly (CompiledCuboidParts.Part Base, CompiledCuboidParts.Part Step)[] _states;
    private readonly CompiledCuboidParts.Part[] _quarters;

    private CompiledStairGeometry((CompiledCuboidParts.Part, CompiledCuboidParts.Part)[] states,
        CompiledCuboidParts.Part[] quarters)
    {
        _states = states;
        _quarters = quarters;
    }

    internal bool Draw(Block block, in BlockPos pos, ref BlockRenderContext context, int metadata,
        StairShape.Resolved shape)
    {
        var (basePart, stepPart) = _states[metadata & 7];
        var baseContext = context with { OverrideBounds = basePart.Bounds, CompiledCuboid = basePart.Geometry };
        var selected = SameBounds(shape.Step, stepPart.Bounds) ? stepPart : Quarter(shape.Step);
        var stepContext = context with { OverrideBounds = selected.Bounds, CompiledCuboid = selected.Geometry };
        var rendered = baseContext.DrawBlock(block, pos) | stepContext.DrawBlock(block, pos);
        if (shape.Extra is { } extra)
        {
            var part = Quarter(extra);
            var extraContext = context with { OverrideBounds = part.Bounds, CompiledCuboid = part.Geometry };
            rendered |= extraContext.DrawBlock(block, pos);
        }
        return rendered;
    }

    private CompiledCuboidParts.Part Quarter(Box box) => _quarters[
        (box.MinY == 0 ? 4 : 0) + (box.MinZ == 0 ? 0 : 2) + (box.MinX == 0 ? 0 : 1)];

    internal static CompiledStairGeometry Build(CompiledBlockModel model,
        IReadOnlyDictionary<RenderResourceId, int> fixedLayers)
    {
        var names = new[] { "base_lower", "base_upper", "step_0", "step_1", "step_2", "step_3", "step_4", "step_5", "step_6", "step_7",
            "quarter_0", "quarter_1", "quarter_2", "quarter_3", "quarter_4", "quarter_5", "quarter_6", "quarter_7" };
        var parts = CompiledCuboidParts.Build(model, fixedLayers, names);
        var quarters = new CompiledCuboidParts.Part[8];
        for (var i = 0; i < quarters.Length; i++)
        {
            quarters[i] = parts.Get("quarter_" + i);
            var expected = new Box((i & 1) == 0 ? 0 : .5, (i & 4) == 0 ? .5 : 0,
                (i & 2) == 0 ? 0 : .5, (i & 1) == 0 ? .5 : 1,
                (i & 4) == 0 ? 1 : .5, (i & 2) == 0 ? .5 : 1);
            if (!SameBounds(quarters[i].Bounds, expected))
                throw new InvalidDataException($"Stair model '{model.Id}' quarter {i}: visual bounds must match the corner collision shape.");
        }
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
        return new CompiledStairGeometry(states, quarters);
    }

    private static bool SameBounds(Box left, Box right) =>
        left.MinX == right.MinX && left.MinY == right.MinY && left.MinZ == right.MinZ &&
        left.MaxX == right.MaxX && left.MaxY == right.MaxY && left.MaxZ == right.MaxZ;
}
