using System.Collections.Frozen;
using OmniBlock.Blocks;
using OmniBlock.Textures;
using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>
/// Immutable session snapshot captured at mesh admission. Installed client state definitions select
/// models: procedural geometry remains authoritative for blocks outside that catalog. Fixed terrain
/// material identities let resident old meshes remain drawable throughout a resource replacement.
/// </summary>
internal sealed class BlockModelBindings
{
    private readonly FrozenDictionary<(int Block, int Meta), Binding> _states;
    private sealed record Binding(CompiledCuboidGeometry Geometry, bool LegacyGreedyCompatible);

    private BlockModelBindings(long generation, Dictionary<(int, int), Binding> states, CompiledFenceGeometry? fence)
    {
        Generation = generation;
        _states = states.ToFrozenDictionary();
        Fence = fence;
    }

    internal long Generation { get; }
    internal CompiledFenceGeometry? Fence { get; }
    internal CompiledCuboidGeometry? Get(int block, int meta) =>
        _states.TryGetValue((block, meta), out var binding) ? binding.Geometry : null;
    internal bool AllowsLegacyGreedy(int block, int meta) =>
        !_states.TryGetValue((block, meta), out var binding) || binding.LegacyGreedyCompatible;

    internal static BlockModelBindings Build(long generation, IBlockRuntimeView blocks,
        BlockModelCatalog models, IReadOnlyDictionary<RenderResourceId, int> fixedLayers, BlockStateDefinitions definitions,
        FencePartDefinitions? fence = null)
    {
        var states = new Dictionary<(int, int), Binding>();
        var geometry = new Dictionary<RenderResourceId, CompiledCuboidGeometry>();
        foreach (ref readonly var definition in definitions.States)
        {
            var bottom = definition.Shape == CuboidStateShape.UpperSlab ? .5 : 0;
            var top = definition.Shape == CuboidStateShape.LowerSlab ? .5 : 1;
            Add(definition.Block, definition.Metadata, definition.Model, bottom, top);
        }
        CompiledFenceGeometry? compiledFence = null;
        if (fence is not null)
        {
            var block = blocks.Get("omniblock:fence");
            if (block.RenderType != BlockRendererType.Fence)
                throw new InvalidDataException("Fence parts 'omniblock:fence': target must use Fence rendering.");
            CompiledBlockModel model;
            try { model = models.Get(fence.Model); }
            catch (KeyNotFoundException ex)
            {
                throw new InvalidDataException($"Fence parts 'omniblock:fence': unknown model '{fence.Model}'.", ex);
            }
            compiledFence = CompiledFenceGeometry.Build(model, fence, fixedLayers);
        }
        return new BlockModelBindings(generation, states, compiledFence);

        void Add(ResourceLocation blockName, int meta, RenderResourceId modelName, double bottom, double top)
        {
            if (!blocks.TryGet(blockName, out var block))
                throw new InvalidDataException($"Block states '{blockName}' state {meta}: unknown block for model '{modelName}'.");
            if (block.RenderType != BlockRendererType.Standard)
                throw new InvalidDataException($"Block states '{blockName}' state {meta}: cuboid adapter requires Standard rendering.");
            CompiledBlockModel model;
            try { model = models.Get(modelName); }
            catch (KeyNotFoundException ex) { throw new InvalidDataException($"Block states '{blockName}' state {meta}: unknown model '{modelName}'.", ex); }
            var expected = CompiledCuboidGeometry.ForBounds(new Box(0, bottom, 0, 1, top, 1))!;
            if (!model.AmbientOcclusion || model.Quads.Length != 6)
                throw Invalid("expected six cuboid faces with ambient occlusion");
            var sides = new HashSet<Side>();
            var greedy = bottom == 0 && top == 1;
            foreach (ref readonly var face in model.Quads)
            {
                ref readonly var original = ref expected.Face(face.Direction);
                Side? expectedCull = face.Direction == Side.Up && top < 1 || face.Direction == Side.Down && bottom > 0
                    ? null : face.Direction;
                if (!sides.Add(face.Direction) || face.A.Position != original.A.Position ||
                    face.B.Position != original.B.Position || face.C.Position != original.C.Position ||
                    face.D.Position != original.D.Position || !face.Shade || face.TintIndex != -1 ||
                    face.CullFace != expectedCull)
                    throw Invalid("shape, tint, shade and culling must match the initial cuboid binding");
                if (!fixedLayers.TryGetValue(face.Texture, out var layer) || layer != face.ArrayLayer)
                    throw Invalid($"texture '{face.Texture}' requires an unsupported new terrain material slot");
                greedy &= face.ArrayLayer == Atlases.Terrain.LayerOfGridIndex(block.GetTexture(face.Direction, meta)) &&
                    face.A.Uv == original.A.Uv && face.B.Uv == original.B.Uv &&
                    face.C.Uv == original.C.Uv && face.D.Uv == original.D.Uv;
            }
            if (!geometry.TryGetValue(model.Id, out var compiled))
                geometry.Add(model.Id, compiled = new CompiledCuboidGeometry(model, true));
            states.Add((block.Id, meta), new Binding(compiled, greedy));
            InvalidDataException Invalid(string reason) => new($"Block '{blockName}' state {meta}, model '{model.Id}': {reason}.");
        }
    }
}
