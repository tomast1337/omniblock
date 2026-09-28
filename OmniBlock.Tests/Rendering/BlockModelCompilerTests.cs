using System.Numerics;
using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks;
using OmniBlock.Client.Rendering.Blocks.Models;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Textures;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Tests.Rendering;

public sealed class BlockModelCompilerTests
{
    private static readonly ResourceLocation ModelId = ResourceLocation.Parse("example:test_cube");
    private const string Plane = """
        {"textures":{"all":"omniblock:stone"},"elements":[
          {"from":[0,8,0],"to":[16,8,16],"faces":{
            "up":{"texture":"#all","uv":[0,0,16,16],"tintindex":2}
          }}]}
        """;

    [Theory]
    [InlineData("stone", 0, 0, 16)]
    [InlineData("slab", 0, 0, 8)]
    [InlineData("slab", 1, 0, 8)]
    [InlineData("slab", 2, 0, 8)]
    [InlineData("slab", 3, 0, 8)]
    [InlineData("slab", 8, 8, 16)]
    [InlineData("double_slab", 0, 0, 16)]
    public void Compiled_cuboids_match_existing_renderer_positions_uvs_and_materials(string blockName, int meta, int minY, int maxY)
    {
        var world = new FakeWorldContext();
        var block = world.Content.Blocks.Get(blockName);
        world.Writer.SetBlock(0, 64, 0, block.Id, meta);
        var textures = blockName == "stone" ? new[] { "stone", "stone", "stone" }
            : meta >= 8 ? new[] { "stone_slab_side", "stone_slab_side", "stone_slab_side" }
            : meta switch
            {
                1 => ["sandstone_bottom", "sandstone_top", "sandstone_side"],
                2 => ["wooden_planks", "wooden_planks", "wooden_planks"],
                3 => ["cobblestone", "cobblestone", "cobblestone"],
                _ => ["stone_slab_top", "stone_slab_top", "stone_slab_side"]
            };
        var model = Compile(Cuboid(minY, maxY, textures));
        var sink = new GeometrySink();
        Assert.True(BlockRenderer.RenderBlockByRenderType(world.Reader, world.Content.Blocks, new FullLight(),
            block, new BlockPos(0, 64, 0), sink));
        Assert.Equal(6, model.Quads.Length);
        foreach (var quad in model.Quads)
        {
            var actual = sink.Vertices.Where(v => v.Side == quad.Direction).ToArray();
            var expected = new[] { quad.A, quad.B, quad.C, quad.D };
            Assert.Equal(4, actual.Length);
            for (var i = 0; i < 4; i++)
            {
                Assert.Equal(expected[i].Position + new Vector3(0, 64, 0), actual[i].Position);
                Assert.Equal(expected[i].Uv, actual[i].Uv);
                Assert.Equal(quad.ArrayLayer, actual[i].Layer);
            }
            Assert.Equal(-1, quad.TintIndex);
            Assert.True(quad.Shade);
        }
    }

    [Fact]
    public void Plane_retains_tint_and_ao_without_adding_a_back_face_or_neighbor_cull()
    {
        var model = Compile(Plane.Replace("\"elements\"", "\"ambientocclusion\":false,\"elements\""));
        var quad = Assert.Single(model.Quads.ToArray());
        Assert.False(model.AmbientOcclusion);
        Assert.Equal(2, quad.TintIndex);
        Assert.Null(quad.CullFace);
        Assert.Equal(.5f, quad.A.Position.Y);
        Assert.Equal(Vector3.UnitY, Vector3.Cross(quad.B.Position - quad.A.Position, quad.C.Position - quad.A.Position));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void Face_rotation_cycles_uv_corners_without_moving_geometry(int rotation)
    {
        var original = Compile(Plane).Quads[0];
        var rotated = Compile(Plane.Replace("\"tintindex\":2", $"\"rotation\":{rotation},\"tintindex\":2")).Quads[0];
        var before = new[] { original.A, original.B, original.C, original.D };
        var after = new[] { rotated.A, rotated.B, rotated.C, rotated.D };
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(before[i].Position, after[i].Position);
            Assert.Equal(before[(i + rotation / 90) % 4].Uv, after[i].Uv);
        }
    }

    [Fact]
    public void Boundary_cullface_is_retained_but_inset_face_culling_is_rejected()
    {
        var boundary = Plane.Replace("[0,8,0]", "[0,16,0]").Replace("[16,8,16]", "[16,16,16]")
            .Replace("\"tintindex\":2", "\"cullface\":\"up\"");
        Assert.Equal(Side.Up, Compile(boundary).Quads[0].CullFace);
        Fails(Plane.Replace("\"tintindex\":2", "\"cullface\":\"up\""), "block boundary");
        Fails(boundary.Replace("\"cullface\":\"up\"", "\"cullface\":\"down\""), "block boundary");
    }

    [Theory]
    [InlineData("\"tintindex\":2", "\"rotation\":45", "rotation")]
    [InlineData("\"tintindex\":2", "\"tintindex\":256", "tintindex")]
    [InlineData("\"tintindex\":2", "\"cullface\":\"sideways\"", "sideways")]
    [InlineData("[0,8,0]", "[17,8,0]", "coordinates")]
    [InlineData("[0,8,0]", "[0,9,0]", "from")]
    [InlineData("[0,8,0]", "[0,1e100,0]", "finite")]
    [InlineData("[16,8,16]", "[0,8,16]", "line/point")]
    [InlineData("\"up\":", "\"north\":", "zero-area")]
    [InlineData("\"texture\":\"#all\"", "\"texture\":\"#missing\"", "#missing")]
    [InlineData("\"all\":\"omniblock:stone\"", "\"all\":\"#all\"", "cyclic")]
    [InlineData("omniblock:stone", "example:missing", "example:missing")]
    [InlineData("omniblock:stone", "stone", "namespaced")]
    [InlineData("\"uv\":[0,0,16,16],", "", "uv")]
    [InlineData("\"uv\":[0,0,16,16]", "\"uv\":[0,0,17,16]", "coordinates")]
    [InlineData("\"tintindex\":2", "\"tintindex\":2,\"tintindex\":3", "duplicate")]
    [InlineData("\"tintindex\":2", "\"glow\":true", "unsupported")]
    public void Invalid_definition_has_owner_and_specific_diagnostic(string before, string after, string message)
    {
        Fails(Plane.Replace(before, after), message);
    }

    [Theory]
    [InlineData("parent", "\"example:base\"")]
    [InlineData("display", "{}")]
    [InlineData("loader", "\"example:custom\"")]
    public void Unsupported_features_fail_instead_of_silently_changing_the_model(string key, string value) =>
        Fails(Plane.Replace("\"textures\":", $"\"{key}\":{value},\"textures\":"), key);

    [Fact]
    public void Rotated_elements_are_explicitly_unsupported() =>
        Fails(Plane.Replace("\"from\":", "\"rotation\":{\"angle\":45,\"axis\":\"y\"},\"from\":"), "rotation");

    [Fact]
    public void Alias_chains_resolve_and_unused_bad_dependencies_fail()
    {
        var chain = Plane.Replace("\"all\":\"omniblock:stone\"", "\"all\":\"#base\",\"base\":\"omniblock:stone\"");
        Assert.Equal(ResourceLocation.Parse("omniblock:stone"), Compile(chain).Quads[0].Texture);
        Fails(chain.Replace("\"base\":\"omniblock:stone\"", "\"base\":\"#all\""), "cyclic");
        Fails(Plane.Replace("\"all\":", "\"unused\":\"example:missing\",\"all\":"), "example:missing");
    }

    [Fact]
    public void Limits_reject_oversized_inputs_and_out_of_range_material_layers()
    {
        Fails(new string(' ', BlockModelCompiler.MaximumJsonCharacters + 1), "size limit");
        using var doc = JsonDocument.Parse(Plane);
        var element = doc.RootElement.GetProperty("elements")[0].GetRawText();
        Fails(Plane.Replace(element, string.Join(',', Enumerable.Repeat(element, BlockModelCompiler.MaximumElements + 1))), "elements");
        foreach (var layer in new[] { -1, 0, 256 })
            Assert.Contains("layer", Assert.Throws<InvalidDataException>(() => BlockModelCompiler.Compile(ModelId, Plane, _ => layer)).Message);
    }

    [Fact]
    public void Catalog_candidate_failure_leaves_the_previous_snapshot_and_its_geometry_unchanged()
    {
        var definitions = new[] { KeyValuePair.Create(ModelId, Plane) };
        var active = BlockModelCatalog.Build(definitions, Resolve);
        var previous = active;
        var original = active.Get(ModelId).Quads.ToArray();
        var bad = KeyValuePair.Create(ResourceLocation.Parse("example:broken"), "{}");
        Assert.Throws<InvalidDataException>(() => active = BlockModelCatalog.Build([definitions[0], bad], Resolve));
        Assert.Same(previous, active);
        Assert.Equal(original, active.Get(ModelId).Quads.ToArray());
        Assert.Throws<InvalidDataException>(() => BlockModelCatalog.Build([definitions[0], definitions[0]], Resolve));

        var independent = BlockModelCatalog.Build(definitions, Resolve);
        Assert.NotSame(active.Get(ModelId), independent.Get(ModelId));
        definitions[0] = bad;
        original[0] = default; // Neither the source collection nor an output copy can mutate a model.
        Assert.Equal(Side.Up, active.Get(ModelId).Quads[0].Direction);
        Assert.Equal(1, active.Count);
    }

    [Fact]
    public void Texture_variable_and_catalog_limits_are_enforced()
    {
        var textures = Enumerable.Range(0, BlockModelCompiler.MaximumTextureVariables + 1)
            .ToDictionary(i => "t" + i, _ => "omniblock:stone");
        Fails(JsonSerializer.Serialize(new { textures, elements = Array.Empty<object>() }), "texture variables");
        var definitions = Enumerable.Range(0, BlockModelCatalog.MaximumModels + 1)
            .Select(i => KeyValuePair.Create(ResourceLocation.Parse("example:m" + i), Plane));
        Assert.Contains("exceeds", Assert.Throws<InvalidDataException>(() => BlockModelCatalog.Build(definitions, Resolve)).Message);
    }

    [Fact]
    public void Source_face_order_does_not_change_compiled_geometry()
    {
        var json = Cuboid(0, 16, ["stone", "stone", "stone"]);
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        var faces = node["elements"]![0]!["faces"]!.AsObject();
        var reverse = faces.Reverse().Select(pair => KeyValuePair.Create(pair.Key, pair.Value?.DeepClone())).ToArray();
        faces.Clear();
        foreach (var pair in reverse) faces.Add(pair.Key, pair.Value);
        Assert.Equal(Compile(json).Quads.ToArray(), Compile(node.ToJsonString()).Quads.ToArray());
    }

    [Fact]
    public void Construction_copies_quad_storage_and_explicit_flags_survive()
    {
        var json = Plane.Replace("\"from\":", "\"shade\":false,\"from\":");
        var quads = Compile(json).Quads.ToArray();
        var model = new CompiledBlockModel(ModelId, true, quads);
        var saved = model.Quads[0];
        quads[0] = default;
        Assert.Equal(saved, model.Quads[0]);
        Assert.False(saved.Shade);
    }

    private static string Cuboid(int minY, int maxY, string[] textures)
    {
        var faces = Enum.GetValues<Side>().ToDictionary(side => side.ToString().ToLowerInvariant(), side => new
        {
            texture = "omniblock:" + textures[side == Side.Down ? 0 : side == Side.Up ? 1 : 2],
            uv = side is Side.Down or Side.Up ? new[] { 0, 0, 16, 16 } : new[] { 0, 16 - maxY, 16, 16 - minY }
        });
        return JsonSerializer.Serialize(new { elements = new[] { new { from = new[] { 0, minY, 0 }, to = new[] { 16, maxY, 16 }, faces } } });
    }

    private static CompiledBlockModel Compile(string json) => BlockModelCompiler.Compile(ModelId, json, Resolve);
    private static int Resolve(ResourceLocation id) => id.IsVanilla ? Atlases.Terrain.LayerOf(id.ToString()) : throw new KeyNotFoundException($"Unknown texture '{id}'.");
    private static void Fails(string json, string message)
    {
        var error = Assert.Throws<InvalidDataException>(() => Compile(json));
        Assert.Contains(ModelId.ToString(), error.Message);
        Assert.Contains(message, error.Message);
    }

    private sealed class FullLight : ILightProvider
    {
        public float GetNaturalBrightness(int x, int y, int z, int minLight) => 1;
        public float GetLuminance(int x, int y, int z) => 1;
        public LightLevels GetLightLevels(int x, int y, int z, int minBlockLight) => LightLevels.FullSky;
    }

    private readonly record struct Vertex(Vector3 Position, Vector2 Uv, int Layer, Side Side);
    private sealed class GeometrySink : IBlockVertexSink
    {
        public List<Vertex> Vertices { get; } = [];
        private int _layer;
        private Side _side;
        public void addVertexWithUV(double x, double y, double z, double u, double v) => Vertices.Add(new(
            new Vector3((float)x, (float)y, (float)z), new Vector2((float)u, (float)v), _layer, _side));
        public void setArrayLayer(int layer) => _layer = layer;
        public void setQuadDirection(Side? side) => _side = side ?? throw new InvalidOperationException();
        public void setColorOpaque_F(float red, float green, float blue) { }
        public void setLight(float sky, float block) { }
        public void setTranslationF(float x, float y, float z) => throw new InvalidOperationException();
    }
}
