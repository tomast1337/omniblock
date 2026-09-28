using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks.Models;

namespace OmniBlock.Tests.Rendering;

public sealed class BlockModelInheritanceTests
{
    private const string Template = """
        {"ambientocclusion":false,"textures":{"side":"#all"},"elements":[
          {"from":[2,4,6],"to":[12,8,14],"faces":{
            "down":{"texture":"#side"},"up":{"texture":"#side"},
            "north":{"texture":"#side"},"south":{"texture":"#side"},
            "west":{"texture":"#side"},"east":{"texture":"#side"}
          }}]}
        """;
    private const string Child = """{"parent":"example:base","textures":{"all":"example:stone"}}""";
    private static readonly RenderResourceId BaseId = RenderResourceId.Parse("example:base");
    private static readonly RenderResourceId ChildId = RenderResourceId.Parse("example:child");
    private static KeyValuePair<RenderResourceId, string> Source(string id, string json) => KeyValuePair.Create(RenderResourceId.Parse(id), json);
    private static int Texture(RenderResourceId id) => id.Path switch
    {
        "stone" => 1, "dirt" => 2, _ => throw new KeyNotFoundException(id.ToString())
    };

    [Fact]
    public void Abstract_parent_aliases_bind_after_child_overrides_regardless_of_definition_order()
    {
        var definitions = new[] { Source("example:base", Template), Source("example:child", Child) };
        var first = BlockModelCatalog.Build(definitions, Texture, [ChildId]);
        var reverse = BlockModelCatalog.Build(definitions.Reverse(), Texture, [ChildId]);
        var model = first.Get(ChildId);
        Assert.Equal(1, first.Count);
        Assert.Throws<KeyNotFoundException>(() => first.Get(BaseId));
        Assert.False(model.AmbientOcclusion);
        Assert.Equal(6, model.Quads.Length);
        Assert.All(model.Quads.ToArray(), q => Assert.Equal(RenderResourceId.Parse("example:stone"), q.Texture));
        Assert.Equal(model.Quads.ToArray(), reverse.Get(ChildId).Quads.ToArray());
        // Without entry points every definition must be renderable: an unbound template is not
        // silently skipped just because its own texture resolution fails.
        Assert.Throws<InvalidDataException>(() => BlockModelCatalog.Build(definitions, Texture));
    }

    [Fact]
    public void Three_level_inheritance_uses_leaf_textures_without_mutating_siblings()
    {
        var leaf = Source("example:leaf", """{"parent":"example:child","ambientocclusion":true,"textures":{"all":"example:dirt"}}""");
        var leafId = leaf.Key;
        var catalog = BlockModelCatalog.Build([Source("example:base", Template), Source("example:child", Child), leaf], Texture, [ChildId, leafId]);
        Assert.False(catalog.Get(ChildId).AmbientOcclusion);
        Assert.True(catalog.Get(leafId).AmbientOcclusion);
        Assert.All(catalog.Get(ChildId).Quads.ToArray(), q => Assert.Equal(1, q.ArrayLayer));
        Assert.All(catalog.Get(leafId).Quads.ToArray(), q => Assert.Equal(2, q.ArrayLayer));
    }

    [Fact]
    public void Explicit_child_elements_replace_instead_of_append_including_an_empty_list()
    {
        foreach (var elements in new[] { "[]", """[{"from":[0,0,0],"to":[16,16,16],"faces":{"up":{"texture":"#all"}}}]""" })
        {
            var child = Child.Replace("\"parent\":", "\"elements\":" + elements + ",\"parent\":");
            var model = BlockModelCatalog.Build([Source("example:base", Template), Source("example:child", child)], Texture, [ChildId]).Get(ChildId);
            Assert.Equal(elements == "[]" ? 0 : 1, model.Quads.Length);
        }
    }

    [Theory]
    [InlineData(Side.Down, 2, 6, 12, 14)]
    [InlineData(Side.Up, 2, 6, 12, 14)]
    [InlineData(Side.North, 4, 8, 14, 12)]
    [InlineData(Side.South, 2, 8, 12, 12)]
    [InlineData(Side.West, 6, 8, 14, 12)]
    [InlineData(Side.East, 2, 8, 10, 12)]
    public void Default_uvs_project_asymmetric_bounds_using_existing_face_orientation(Side side, int u0, int v0, int u1, int v1)
    {
        var bound = Template.Replace("\"side\":\"#all\"", "\"side\":\"example:stone\"");
        var implicitModel = BlockModelCompiler.Compile(BaseId, bound, Texture);
        var json = JsonNode.Parse(bound)!;
        var face = json["elements"]![0]!["faces"]![side.ToString().ToLowerInvariant()]!;
        face["uv"] = JsonSerializer.SerializeToNode(new[] { u0, v0, u1, v1 });
        var explicitModel = BlockModelCompiler.Compile(BaseId, json.ToJsonString(), Texture);
        var actual = implicitModel.Quads.ToArray().Single(q => q.Direction == side);
        var expected = explicitModel.Quads.ToArray().Single(q => q.Direction == side);
        Assert.Equal(expected, actual);
        var uvs = new[] { actual.A.Uv, actual.B.Uv, actual.C.Uv, actual.D.Uv };
        Assert.Equal(new Vector2(u0, v0) / 16, new Vector2(uvs.Min(uv => uv.X), uvs.Min(uv => uv.Y)));
        Assert.Equal(new Vector2(u1, v1) / 16, new Vector2(uvs.Max(uv => uv.X), uvs.Max(uv => uv.Y)));
    }

    [Fact]
    public void Default_uvs_still_obey_face_rotation_and_explicit_uvs_take_precedence()
    {
        var bound = Template.Replace("\"side\":\"#all\"", "\"side\":\"example:stone\"");
        var model = BlockModelCompiler.Compile(BaseId, bound, Texture);
        var rotated = BlockModelCompiler.Compile(BaseId, bound.Replace("\"texture\":", "\"rotation\":90,\"texture\":"), Texture);
        for (var i = 0; i < model.Quads.Length; i++)
            Assert.Equal(model.Quads[i].B.Uv, rotated.Quads[i].A.Uv);
        var explicitModel = BlockModelCompiler.Compile(BaseId, bound.Replace("\"texture\":", "\"uv\":[0,0,16,16],\"texture\":"), Texture);
        Assert.NotEqual(model.Quads[0].A.Uv, explicitModel.Quads[0].A.Uv);
    }

    [Fact]
    public void Missing_parent_and_cycles_report_the_chain_including_unselected_templates()
    {
        var missing = Assert.Throws<InvalidDataException>(() => BlockModelCatalog.Build([Source("example:child", Child)], Texture));
        Assert.Contains("example:child", missing.Message);
        Assert.Contains("example:base", missing.Message);
        var cycle = Assert.Throws<InvalidDataException>(() => BlockModelCatalog.Build([
            Source("example:base", """{"parent":"example:child"}"""), Source("example:child", Child)], Texture, []));
        Assert.Contains("parent cycle", cycle.Message);
        Assert.Contains("example:base -> example:child -> example:base", cycle.Message);
        Assert.Throws<InvalidDataException>(() => BlockModelCatalog.Build([Source("example:base", """{"parent":"example:base"}""")], Texture));
    }

    [Fact]
    public void Parent_depth_is_bounded_independent_of_enumeration_order()
    {
        var chain = Enumerable.Range(0, BlockModelDefinitions.MaximumInheritanceDepth + 1)
            .Select(i => Source("example:m" + i, i == 0 ? """{"elements":[]}""" : "{\"parent\":\"example:m" + (i - 1) + "\"}")).ToArray();
        Assert.Equal(32, BlockModelCatalog.Build(chain.Take(32), Texture).Count);
        foreach (var definitions in new[] { chain, chain.Reverse().ToArray() })
            Assert.Contains("inheritance exceeds", Assert.Throws<InvalidDataException>(() => BlockModelCatalog.Build(definitions, Texture)).Message);
    }

    [Fact]
    public void Inherited_texture_limit_cannot_be_bypassed_by_splitting_bindings_between_parents()
    {
        var textures = Enumerable.Range(0, BlockModelCompiler.MaximumTextureVariables)
            .ToDictionary(i => "t" + i, _ => "example:stone");
        var parent = JsonSerializer.Serialize(new { textures, elements = Array.Empty<object>() });
        Assert.Contains("texture variables", Assert.Throws<InvalidDataException>(() => BlockModelCatalog.Build([
            Source("example:base", parent), Source("example:child", Child)], Texture, [ChildId])).Message);
    }

    [Theory]
    [InlineData("{\"parent\":null}")]
    [InlineData("{\"parent\":\"base\"}")]
    [InlineData("{\"parent\":\"example:base\",\"parent\":\"example:base\"}")]
    [InlineData("{\"elements\":[],\"ambientocclusion\":\"false\"}")]
    public void Invalid_inheritance_source_keeps_the_published_candidate_unchanged(string broken)
    {
        var sources = new[] { Source("example:base", Template), Source("example:child", Child) };
        var active = BlockModelCatalog.Build(sources, Texture, [ChildId]);
        var previous = active;
        Assert.Throws<InvalidDataException>(() => active = BlockModelCatalog.Build(
            [sources[0], Source("example:child", broken)], Texture, [ChildId]));
        Assert.Same(previous, active);
    }
}
