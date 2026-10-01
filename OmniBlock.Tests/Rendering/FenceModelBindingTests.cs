using System.Text;
using System.Text.Json;
using OmniBlock.Client.Rendering.Blocks.Models;
using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class FenceModelBindingTests
{
    private const string StatePath = FencePartDefinitions.Path;
    private const string ModelPath = "assets/omniblock/models/block/fence.json";
    private static Stream Bytes(string value) => new MemoryStream(Encoding.UTF8.GetBytes(value));
    private static string Installed(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));

    [Theory]
    [InlineData("\"when\": { \"neighbor.west\": true }", "\"when\": { \"neighbor.diagonal\": true }", "unknown condition")]
    [InlineData("\"element\": \"west_lower\"", "\"element\": \"post\"", "duplicate")]
    [InlineData("\"when\": \"always\"", "\"when\": \"east\"", "missing unconditional")]
    public void Invalid_fence_state_fails_before_publication(string from, string to, string reason)
    {
        var json = Installed(StatePath).Replace(from, to, StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => FencePartDefinitions.Load(
            path => path == StatePath ? Bytes(json) : null, BlockModelBindingTests.OpenInstalled));
        Assert.Contains("omniblock:fence", error.Message);
        Assert.Contains(reason, error.Message);
    }

    [Fact]
    public void An_unconditional_rail_cannot_substitute_for_the_required_post()
    {
        var json = Installed(StatePath)
            .Replace("\"element\": \"post\", \"when\": \"always\"",
                "\"element\": \"post\", \"when\": { \"neighbor.east\": true }", StringComparison.Ordinal)
            .Replace("\"element\": \"west_lower\", \"when\": { \"neighbor.west\": true }",
                "\"element\": \"west_lower\", \"when\": \"always\"", StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => FencePartDefinitions.Load(
            path => path == StatePath ? Bytes(json) : null, BlockModelBindingTests.OpenInstalled));
        Assert.Contains("missing unconditional post", error.Message);
    }

    [Fact]
    public void Named_condition_inputs_support_conjunction_and_negation_without_world_access()
    {
        var inputs = new Dictionary<string, int>
        {
            ["neighbor.west"] = 1, ["neighbor.north"] = 2
        };
        using var document = JsonDocument.Parse("""
            {"neighbor.west":true,"neighbor.north":false}
            """);
        var condition = BlockModelCondition.Parse(document.RootElement, inputs);
        Assert.False(condition.IsUnconditional);
        Assert.True(condition.Matches(1));
        Assert.False(condition.Matches(0));
        Assert.False(condition.Matches(3));
    }

    [Theory]
    [InlineData("{}", "empty condition")]
    [InlineData("{\"neighbor.west\":1}", "must be boolean")]
    [InlineData("{\"neighbor.west\":true,\"neighbor.west\":false}", "duplicate condition")]
    [InlineData("{\"neighbor.west\":true,\"west\":false}", "requires and forbids")]
    public void Invalid_condition_objects_are_rejected(string json, string reason)
    {
        using var document = JsonDocument.Parse(json);
        var inputs = new Dictionary<string, int>
        {
            ["neighbor.west"] = 1, ["west"] = 1
        };
        var error = Assert.Throws<InvalidDataException>(() =>
            BlockModelCondition.Parse(document.RootElement, inputs));
        Assert.Contains(reason, error.Message);
    }

    [Theory]
    [InlineData("\"name\": \"west_lower\"", "\"name\": \"missing_west_lower\"", "missing named element")]
    [InlineData("\"to\": [6,9,9]", "\"to\": [17,9,9]", "element")]
    public void Invalid_model_parts_reject_the_candidate(string from, string to, string reason)
    {
        var json = Installed(ModelPath).Replace(from, to, StringComparison.Ordinal);
        var world = new FakeWorldContext();
        var error = Assert.Throws<InvalidDataException>(() => BlockModelBindingTests.Build(world.Content.Blocks,
            overrides: path => path == ModelPath ? Bytes(json) : null));
        Assert.Contains("omniblock:block/fence", error.Message);
        Assert.Contains(reason, error.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Captured_cross_chunk_neighbor_has_identical_mesh_with_compiled_parts(bool connected)
    {
        var world = new FakeWorldContext();
        var fence = world.Content.Blocks.Get("fence");
        var left = world.ChunkHost.GetChunk(0, 0);
        var right = world.ChunkHost.GetChunk(1, 0);
        left.Blocks[ChuckFormat.GetIndex(15, 64, 7)] = (byte)fence.Id;
        if (connected) right.Blocks[ChuckFormat.GetIndex(0, 64, 7)] = (byte)fence.Id;
        using var snapshot = new WorldRegionSnapshot(world, -1, 63, -1, 16, 80, 16);
        using var generator = new ChunkMeshGenerator();
        var models = BlockModelBindingTests.Build(world.Content.Blocks);
        using var oldMesh = Build(null);
        using var newMesh = Build(models);
        Assert.Equal(oldMesh.Pages.Length, newMesh.Pages.Length);
        for (var i = 0; i < oldMesh.Pages.Length; i++)
        {
            Assert.Equal(oldMesh.Pages[i].Solid?.Span.ToArray(), newMesh.Pages[i].Solid?.Span.ToArray());
            Assert.Equal(oldMesh.Pages[i].SolidLighting?.InitialValues, newMesh.Pages[i].SolidLighting?.InitialValues);
            Assert.Equal(oldMesh.Pages[i].SolidRanges, newMesh.Pages[i].SolidRanges);
        }

        MeshBuildResult Build(BlockModelBindings? bindings) => generator.GenerateMesh(
            new Vector3D<int>(0, 64, 0), 1, snapshot, false, SectionMeshRebuildPlan.Full,
            CancellationToken.None, bindings);
    }
}
