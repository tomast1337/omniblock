using OmniBlock.Blocks;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>
/// Immutable, material-neutral geometry for the first live model migration. The standard block
/// renderer still resolves per-state textures, culling and lighting. These three fixed templates
/// are compiled once, not parsed or allocated by each block/mesh. They are NOT pack model bindings.
/// </summary>
internal sealed class CompiledCuboidGeometry
{
    private readonly CompiledBlockQuad[] _faces;
    private static readonly CompiledCuboidGeometry Cube = Build("cube", 0, 16);
    private static readonly CompiledCuboidGeometry LowerSlab = Build("slab_lower", 0, 8);
    private static readonly CompiledCuboidGeometry UpperSlab = Build("slab_upper", 8, 16);

    internal CompiledCuboidGeometry(CompiledBlockModel model, bool useModelMaterials = false)
    {
        UseModelMaterials = useModelMaterials;
        _faces = new CompiledBlockQuad[6];
        foreach (ref readonly var quad in model.Quads) _faces[(int)quad.Direction] = quad;
    }

    internal bool UseModelMaterials { get; }

    internal ref readonly CompiledBlockQuad Face(Side side) => ref _faces[(int)side];

    internal static CompiledCuboidGeometry? ForBounds(Box bounds)
    {
        if (bounds.MinX != 0 || bounds.MaxX != 1 || bounds.MinZ != 0 || bounds.MaxZ != 1) return null;
        if (bounds.MinY == 0) return bounds.MaxY == 1 ? Cube : bounds.MaxY == .5 ? LowerSlab : null;
        return bounds.MinY == .5 && bounds.MaxY == 1 ? UpperSlab : null;
    }

    private static CompiledCuboidGeometry Build(string name, int minY, int maxY)
    {
        // The material is deliberately symbolic. It never binds a GPU layer: the existing block
        // material resolver supplies that at emission (including metadata and breaking overlays).
        var json = $$"""
            {"textures":{"all":"omniblock:geometry_placeholder"},"elements":[{
              "from":[0,{{minY}},0],"to":[16,{{maxY}},16],"faces":{
                "down":{"texture":"#all"},"up":{"texture":"#all"},
                "north":{"texture":"#all"},"south":{"texture":"#all"},
                "west":{"texture":"#all"},"east":{"texture":"#all"}
              }
            }]}
            """;
        return new(BlockModelCompiler.Compile(RenderResourceId.Parse("omniblock:builtin_geometry/" + name), json, _ => 1));
    }
}
