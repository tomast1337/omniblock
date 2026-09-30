using System.Text;
using System.Text.Json.Nodes;
using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks.Models;
using OmniBlock.Textures;

namespace OmniBlock.Tests.Rendering;

public sealed class BlockStateDefinitionTests
{
    private const string SlabPath = "assets/omniblock/blockstates/slab.json";
    private static string SlabJson => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SlabPath));
    private static Stream Bytes(string value) => new MemoryStream(Encoding.UTF8.GetBytes(value));
    private static BlockStateDefinitions Load(string slab) => BlockStateDefinitions.Load(
        path => path == SlabPath ? Bytes(slab) : null, BlockModelBindingTests.OpenInstalled);
    private static BlockStateDefinitions LoadInstalled(string slab) => BlockStateDefinitions.Load(_ => null,
        path => path == SlabPath ? Bytes(slab) : BlockModelBindingTests.OpenInstalled(path));

    [Fact]
    public void Shipped_definitions_cover_every_metadata_value_and_deduplicate_model_roots()
    {
        var definitions = BlockStateDefinitions.Load(_ => null, BlockModelBindingTests.OpenInstalled);
        Assert.Equal(48, definitions.States.Length);
        Assert.Equal(12, definitions.ModelRoots.Count());
        foreach (var block in new[] { "stone", "slab", "double_slab" })
            Assert.Equal(Enumerable.Range(0, 16), definitions.States.ToArray()
                .Where(s => s.Block == ResourceLocation.Parse("omniblock:" + block)).Select(s => s.Metadata));
    }

    [Fact]
    public void Pack_state_selection_changes_only_the_selected_variant_and_keeps_prior_snapshot()
    {
        var world = new FakeWorldContext();
        var previous = BlockModelBindingTests.Build(world.Content.Blocks);
        var root = JsonNode.Parse(SlabJson)!;
        root["variants"]!["0"]!["model"] = "omniblock:block/wooden_slab";
        var next = BlockModelBindingTests.Build(world.Content.Blocks, overrides:
            path => path == SlabPath ? Bytes(root.ToJsonString()) : null);
        var slab = world.Content.Blocks.Get("slab").Id;
        Assert.Equal(Layer("wooden_planks"), next.Get(slab, 0)!.Face(Side.Up).ArrayLayer);
        Assert.Equal(Layer("stone_slab_top"), previous.Get(slab, 0)!.Face(Side.Up).ArrayLayer);
        Assert.Equal(previous.Get(slab, 1)!.Face(Side.Up), next.Get(slab, 1)!.Face(Side.Up));
        Assert.False(next.AllowsLegacyGreedy(slab, 0));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("16")]
    [InlineData("00")]
    [InlineData("meta=0")]
    [InlineData("*")]
    public void Invalid_selectors_fail_with_the_owning_block(string selector)
    {
        var json = SlabJson.Replace("\"0\":", $"\"{selector}\":", StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => Load(json));
        Assert.Contains("omniblock:slab", error.Message);
        Assert.Contains(selector, error.Message);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unknown_field")]
    [InlineData("duplicate_field")]
    [InlineData("missing_model")]
    [InlineData("invalid_model_id")]
    [InlineData("shape_change")]
    [InlineData("unknown_shape")]
    [InlineData("multipart")]
    public void Incomplete_ambiguous_or_unsupported_definitions_fail(string kind)
    {
        var root = JsonNode.Parse(SlabJson)!;
        var variants = root["variants"]!.AsObject();
        switch (kind)
        {
            case "missing": root.AsObject().Remove("default"); break;
            case "unknown_field": variants["0"]!["modle"] = "typo"; break;
            case "missing_model": variants["0"]!.AsObject().Remove("model"); root["default"]!.AsObject().Remove("model"); break;
            case "invalid_model_id": variants["0"]!["model"] = "example:../unsafe"; break;
            case "shape_change": variants["0"]!["shape"] = "cube"; break;
            case "unknown_shape": variants["0"]!["shape"] = "stairs"; break;
            case "multipart": root["multipart"] = new JsonArray(); break;
        }
        var json = root.ToJsonString();
        if (kind == "duplicate") json = json.Replace("\"1\":", "\"0\":", StringComparison.Ordinal);
        if (kind == "duplicate_field") json = json.Replace("\"model\":", "\"model\":\"example:one\",\"model\":", StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() =>
            kind is "missing" or "missing_model" ? LoadInstalled(json) : Load(json));
        Assert.Contains("omniblock:slab", error.Message);
        Assert.Contains(SlabPath, error.Message);
    }

    [Fact]
    public void Sparse_pack_override_inherits_unselected_installed_states()
    {
        var overrideJson = """{"variants":{"0":{"model":"omniblock:block/wooden_slab"}}}""";
        var selected = Load(overrideJson);
        var installed = BlockStateDefinitions.Load(_ => null, BlockModelBindingTests.OpenInstalled);
        var slab = ResourceLocation.Parse("omniblock:slab");
        Assert.Equal(RenderResourceId.Parse("omniblock:block/wooden_slab"),
            selected.States.ToArray().Single(s => s.Block == slab && s.Metadata == 0).Model);
        Assert.Equal(installed.States.ToArray().Single(s => s.Block == slab && s.Metadata == 1),
            selected.States.ToArray().Single(s => s.Block == slab && s.Metadata == 1));
    }

    [Fact]
    public void Overlapping_range_selectors_are_rejected()
    {
        var error = Assert.Throws<InvalidDataException>(() => Load("""
            {"variants":{"0..3":{"model":"omniblock:block/stone_slab"},
                         "3":{"model":"omniblock:block/wooden_slab"}}}
            """));
        Assert.Contains("duplicate metadata selector '3'", error.Message);
    }

    [Fact]
    public void Missing_model_dependency_fails_instead_of_using_the_old_model()
    {
        var root = JsonNode.Parse(SlabJson)!;
        root["variants"]!["0"]!["model"] = "example:block/missing";
        var error = Assert.Throws<InvalidDataException>(() => BlockModelBindingTests.Build(
            new FakeWorldContext().Content.Blocks, overrides: path => path == SlabPath ? Bytes(root.ToJsonString()) : null));
        Assert.Contains("example:block/missing", error.Message);
    }

    [Fact]
    public void Model_bound_shape_must_match_the_declared_state_shape()
    {
        var root = JsonNode.Parse(SlabJson)!;
        root["variants"]!["0"]!["model"] = "omniblock:block/stone";
        var error = Assert.Throws<InvalidDataException>(() => BlockModelBindingTests.Build(
            new FakeWorldContext().Content.Blocks, overrides: path => path == SlabPath ? Bytes(root.ToJsonString()) : null));
        Assert.Contains("omniblock:slab", error.Message);
        Assert.Contains("state 0", error.Message);
        Assert.Contains("omniblock:block/stone", error.Message);
    }

    [Fact]
    public void Corrupt_failed_and_oversized_overrides_never_fall_back()
    {
        Assert.Throws<InvalidDataException>(() => Load("{bad"));
        Assert.Throws<InvalidDataException>(() => Load(new string(' ', BlockStateDefinitions.MaximumFileBytes + 1)));
        var error = Assert.Throws<InvalidDataException>(() => BlockStateDefinitions.Load(
            path => path == SlabPath ? throw new IOException("disk failure") : null, BlockModelBindingTests.OpenInstalled));
        Assert.Contains("disk failure", error.Message);
        Assert.Contains(SlabPath, error.Message);
    }

    [Fact]
    public void Catalog_is_installed_owned_and_duplicate_block_entries_are_rejected()
    {
        var visitedCatalog = false;
        var definitions = BlockStateDefinitions.Load(path =>
        {
            if (path == BlockStateDefinitions.CatalogPath) visitedCatalog = true;
            return null;
        }, BlockModelBindingTests.OpenInstalled);
        Assert.False(visitedCatalog);
        Assert.Equal(48, definitions.States.Length);
        var error = Assert.Throws<InvalidDataException>(() => BlockStateDefinitions.Load(_ => null,
            path => path == BlockStateDefinitions.CatalogPath
                ? Bytes("""{"blocks":["omniblock:stone","omniblock:stone"]}""") : BlockModelBindingTests.OpenInstalled(path)));
        Assert.Contains("duplicate block", error.Message);
    }

    [Theory]
    [InlineData("example:missing", "unknown block")]
    [InlineData("omniblock:torch", "Standard rendering")]
    public void Unknown_or_incompatible_catalog_targets_fail_before_binding(string target, string expected)
    {
        var world = new FakeWorldContext();
        var stone = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/omniblock/blockstates/stone.json"));
        var definitions = BlockStateDefinitions.Load(_ => null, path =>
            Bytes(path == BlockStateDefinitions.CatalogPath ? "{\"blocks\":[\"" + target + "\"]}" : stone));
        var emptyModels = BlockModelCatalog.Build([], _ => 1);
        var error = Assert.Throws<InvalidDataException>(() => BlockModelBindings.Build(1, world.Content.Blocks,
            emptyModels, new Dictionary<RenderResourceId, int>(), definitions));
        Assert.Contains(target, error.Message);
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void Model_lookup_failure_identifies_owning_block_and_metadata()
    {
        var world = new FakeWorldContext();
        var definitions = BlockStateDefinitions.Load(_ => null, BlockModelBindingTests.OpenInstalled);
        var error = Assert.Throws<InvalidDataException>(() => BlockModelBindings.Build(1, world.Content.Blocks,
            BlockModelCatalog.Build([], _ => 1), new Dictionary<RenderResourceId, int>(), definitions));
        Assert.Contains("omniblock:stone", error.Message);
        Assert.Contains("state 0", error.Message);
        Assert.Contains("omniblock:block/stone", error.Message);
    }

    [Fact]
    public void Candidate_loader_disposes_streams_on_both_success_and_failure()
    {
        var opened = new List<MemoryStream>();
        Stream? Open(string path)
        {
            var file = Path.Combine(AppContext.BaseDirectory, path);
            if (!File.Exists(file)) return null;
            var stream = new MemoryStream(File.ReadAllBytes(file));
            opened.Add(stream);
            return stream;
        }
        _ = BlockStateDefinitions.Load(_ => null, Open);
        Assert.All(opened, s => Assert.False(s.CanRead));
        opened.Clear();
        Assert.Throws<InvalidDataException>(() => BlockStateDefinitions.Load(path =>
        {
            if (path != SlabPath) return null;
            var stream = new MemoryStream([0xff, 0xfe]);
            opened.Add(stream);
            return stream;
        }, Open));
        Assert.All(opened, s => Assert.False(s.CanRead));
    }

    private static int Layer(string name) => Atlases.Terrain.LayerOfGridIndex(Atlases.Terrain.IndexOf(name));
}
