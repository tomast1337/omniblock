using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks.Models;
using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Textures;

namespace OmniBlock.Tests.Rendering;

public sealed class BlockModelBindingTests
{
    [Fact]
    public async Task Reload_between_admission_and_upload_keeps_worker_bound_to_old_snapshot()
    {
        var world = new FakeWorldContext();
        var original = Build(world.Content.Blocks);
        var current = original;
        var reads = 0;
        using var workers = new ChunkMeshGenerator(modelSnapshot: () => { reads++; return current; });
        workers.MeshChunk(world, default, 1, false, MeshWorkPriority.Critical);
        Assert.Equal(1, reads);
        current = Build(world.Content.Blocks, generation: 2);
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        MeshBuildResult result;
        while (!workers.TryDequeueMesh(out result))
        {
            Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(10), "Worker did not complete its captured snapshot.");
            await Task.Delay(5);
        }
        using (result)
        {
            Assert.False(result.Cancelled);
            Assert.Equal(1, reads); // worker never consulted the replacement slot
            Assert.Same(original, result.Models);
            Assert.False(workers.HasCurrentResources(result));
        }
        workers.MeshChunk(world, default, 2, false, MeshWorkPriority.Critical);
        while (!workers.TryDequeueMesh(out result))
        {
            Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(10), "Replacement build did not complete.");
            await Task.Delay(5);
        }
        using (result)
        {
            Assert.Same(current, result.Models);
            Assert.True(workers.HasCurrentResources(result));
        }
    }

    internal static BlockModelBindings Build(IBlockRuntimeView blocks, Func<string, string>? changeStone = null, long generation = 1,
        Func<string, Stream?>? overrides = null)
    {
        var layers = Atlases.Terrain.Tiles.ToDictionary(t => RenderResourceId.Parse("omniblock:" + t.Name),
            t => Atlases.Terrain.LayerOfGridIndex(Atlases.Terrain.IndexOf(t.Name)));
        var states = BlockStateDefinitions.Load(overrides ?? (_ => null), OpenInstalled);
        var fence = FencePartDefinitions.Load(overrides ?? (_ => null), OpenInstalled);
        var models = BlockModelPackLoader.Build(states.ModelRoots.Append(fence.Model), path =>
        {
            if (path != "assets/omniblock/models/block/stone.json" || changeStone is null) return overrides?.Invoke(path);
            var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
            return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(changeStone(json)));
        }, OpenInstalled, id => layers.TryGetValue(id, out var layer) ? layer : 255);
        return BlockModelBindings.Build(generation, blocks, models, layers, states, fence);
    }

    internal static Stream? OpenInstalled(string path)
    {
        var file = Path.Combine(AppContext.BaseDirectory, path);
        return File.Exists(file) ? File.OpenRead(file) : null;
    }

    [Fact]
    public void Builtins_keep_the_greedy_fast_path_but_pack_material_or_uv_changes_do_not()
    {
        var blocks = new FakeWorldContext().Content.Blocks;
        var builtins = Build(blocks);
        var stone = blocks.Get("stone").Id;
        Assert.True(builtins.AllowsLegacyGreedy(stone, 0));
        Assert.False(builtins.AllowsLegacyGreedy(blocks.Get("slab").Id, 0));
        Assert.NotNull(builtins.Get(blocks.Get("slab").Id, 1));
        Assert.Null(builtins.Get(blocks.Get("glass").Id, 0));
        var material = Build(blocks, json => json.Replace("omniblock:stone\"", "omniblock:cobblestone\"", StringComparison.Ordinal));
        Assert.False(material.AllowsLegacyGreedy(stone, 0));
        var uv = Build(blocks, json => json.Replace("\"cullface\":", "\"rotation\": 90, \"cullface\":", StringComparison.Ordinal));
        Assert.False(uv.AllowsLegacyGreedy(stone, 0));
        Assert.True(builtins.AllowsLegacyGreedy(stone, 0));
    }

    [Theory]
    [InlineData("\"cullface\": \"up\"", "\"rotation\": 0")]
    [InlineData("\"cullface\": \"up\"", "\"cullface\": \"up\", \"tintindex\": 0")]
    [InlineData("\"elements\":", "\"ambientocclusion\": false, \"elements\":")]
    [InlineData("omniblock:stone\"", "example:unallocated\"")]
    public void Unsupported_pack_bindings_fail_with_the_owning_state(string from, string to)
    {
        var blocks = new FakeWorldContext().Content.Blocks;
        var error = Assert.Throws<InvalidDataException>(() => Build(blocks, json => json.Replace(from, to, StringComparison.Ordinal)));
        Assert.Contains("Block 'omniblock:stone' state 0", error.Message);
        Assert.Contains("omniblock:block/stone", error.Message);
    }

    [Fact]
    public void Queued_results_keep_their_snapshot_and_cannot_install_after_reload_even_with_equal_numbers()
    {
        var blocks = new FakeWorldContext().Content.Blocks;
        var first = Build(blocks);
        var current = first;
        using var workers = new ChunkMeshGenerator(modelSnapshot: () => current);
        var result = new MeshBuildResult { Models = first, Priority = MeshWorkPriority.Critical };
        Assert.True(workers.HasCurrentResources(result));
        current = Build(blocks); // snapshot identity, not only a counter, is authoritative
        Assert.False(workers.HasCurrentResources(result));
        Assert.Same(first, result.Models);
        Assert.True(workers.HasCurrentResources(new MeshBuildResult { Models = current }));
    }

    [Fact]
    public void Partial_page_replacement_cannot_mix_resource_snapshots()
    {
        var blocks = new FakeWorldContext().Content.Blocks;
        var first = Build(blocks);
        var next = Build(blocks, generation: 2);
        var section = new SectionRenderState(default) { PresentationModels = first };
        try
        {
            var result = new MeshBuildResult { Models = first, RebuildPlan = new SectionMeshRebuildPlan(1) };
            Assert.True(section.CanComposeModelPages(result));
            result.Models = next;
            Assert.False(section.CanComposeModelPages(result));
            result.RebuildPlan = SectionMeshRebuildPlan.Full;
            Assert.True(section.CanComposeModelPages(result));
        }
        finally { section.Dispose(MeshCancellationReason.RendererDisposed); }
    }
}
