using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class StairCornerBindingTests
{
    [Fact]
    public void Side_continuation_prevents_an_ambiguous_corner()
    {
        var outer = StairShape.Resolve(0, null, null, 2, null);
        Assert.Equal(StairShape.GetBounds(0).Step, outer.Step);
        // The front neighbor must lie in the stair's facing direction (east here).
        outer = StairShape.Resolve(0, 2, null, null, null);
        Assert.NotEqual(StairShape.GetBounds(0).Step, outer.Step);
        var continued = StairShape.Resolve(0, 2, null, 0, null);
        Assert.Equal(StairShape.GetBounds(0).Step, continued.Step);
        Assert.Null(continued.Extra);

        var inner = StairShape.Resolve(0, null, 2, null, null);
        Assert.NotNull(inner.Extra);
        continued = StairShape.Resolve(0, null, 2, 0, null);
        Assert.Null(continued.Extra);
    }

    [Fact]
    public void Cross_chunk_outer_corner_has_the_same_compiled_mesh_as_the_procedural_path()
    {
        var world = new FakeWorldContext();
        var wooden = world.Content.Blocks.Get("wooden_stairs");
        var cobble = world.Content.Blocks.Get("cobblestone_stairs");
        var left = world.ChunkHost.GetChunk(0, 0);
        var right = world.ChunkHost.GetChunk(1, 0);
        left.Blocks[ChuckFormat.GetIndex(15, 64, 7)] = (byte)wooden.Id;
        right.Blocks[ChuckFormat.GetIndex(0, 64, 7)] = (byte)cobble.Id;
        right.Meta.SetNibble(0, 64, 7, 2);
        using var snapshot = new WorldRegionSnapshot(world, -1, 63, -1, 16, 80, 16);
        var shape = StairShape.Resolve(snapshot, snapshot.ContentBlocks, 15, 64, 7, 0);
        Assert.Equal(new Box(.5, .5, .5, 1, 1, 1), shape.Step);

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

        MeshBuildResult Build(OmniBlock.Client.Rendering.Blocks.Models.BlockModelBindings? bindings) =>
            generator.GenerateMesh(new Vector3D<int>(0, 64, 0), 1, snapshot, false,
                SectionMeshRebuildPlan.Full, CancellationToken.None, bindings);
    }
}
