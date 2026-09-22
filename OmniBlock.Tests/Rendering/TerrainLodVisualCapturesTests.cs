using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodVisualCapturesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Renderer_preserves_admitted_work_on_unload_but_rejects_same_revision_reload(bool reload)
    {
        var world = new LightTestWorld();
        var chunk = world.Chunks.Add(0, 0, populateLight: false);
        var packet = new byte[ChuckFormat.ChunkSize * 5 / 2];
        packet[ChuckFormat.GetIndex(1, 70, 2)] = (byte)world.Content.Blocks.Get("omniblock:stone").Id;
        chunk.LoadFromPacket(packet, 0, 0, 0, 16, ChuckFormat.WorldHeight, 16, 0);
        using var renderer = new ClientTerrainLodRenderer(world);
        renderer.ObserveRegion(0, 0, 0, 0);
        renderer.Tick(default);
        renderer.Tick(default);
        Assert.Equal(1, renderer.Snapshot.ConversionOwnedColumns);
        world.Chunks.Remove(0, 0);
        if (reload)
        {
            var replacement = world.Chunks.Add(0, 0, populateLight: false);
            replacement.LoadFromPacket(packet, 0, 0, 0, 16, ChuckFormat.WorldHeight, 16, 0);
            Assert.Equal(chunk.TerrainRevision, replacement.TerrainRevision);
        }

        Assert.True(SpinWait.SpinUntil(() =>
        {
            renderer.QueueCompletedConversions(default, 70, 480, 1);
            renderer.Tick(default);
            return renderer.Snapshot.ConversionOwnedColumns == 0;
        }, TimeSpan.FromSeconds(5)));
        Assert.Equal(reload ? 0 : 1, renderer.Snapshot.MeshCompilationOwned);
        Assert.Equal(reload ? 1 : 0, renderer.Snapshot.StaleResults);
    }

    [Fact]
    public void Mutation_invalidates_a_capture_even_after_its_chunk_unloads()
    {
        var world = new FakeWorldContext();
        var chunk = world.ChunkHost.GetChunk(0, 0);
        var lifetime = new TerrainLodSourceLifetime(chunk);
        chunk[1, 70, 2] = world.Content.Blocks.Get("omniblock:stone").Id;
        Assert.False(lifetime.IsCurrent(chunk));
        Assert.False(lifetime.IsCurrent(null));
    }

    [Fact]
    public void Captured_visuals_survive_unload_without_reading_live_arrays_again()
    {
        var world = new FakeWorldContext();
        var chunk = world.ChunkHost.GetChunk(0, 0);
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        chunk.Blocks[ChuckFormat.GetIndex(1, 70, 2)] = (byte)stone;
        using var captures = new TerrainLodVisualCaptures(1);
        var lifetime = new TerrainLodSourceLifetime(chunk);
        captures.Replace((0, 0), new(lifetime, Snapshot(world)));
        chunk.Loaded = false;
        chunk.Blocks[ChuckFormat.GetIndex(1, 70, 2)] = 0;

        Assert.True(captures.TryGet((0, 0), out var captured));
        Assert.True(captured.Lifetime.IsCurrent(null));
        Assert.Equal(stone, captured.Visuals.GetBlockId(1, 70, 2));
        Assert.InRange(captures.RetainedArrayBytes, 1, 128 * 1024);
    }

    [Fact]
    public void Reload_at_same_revision_cannot_accept_old_lifetime()
    {
        var world = new FakeWorldContext();
        var original = world.ChunkHost.GetChunk(0, 0);
        var replacement = new Chunk(world, new byte[ChuckFormat.ChunkSize], 0, 0);
        var lifetime = new TerrainLodSourceLifetime(original);
        Assert.Equal(original.TerrainRevision, replacement.TerrainRevision);
        Assert.True(lifetime.IsCurrent(original));
        Assert.False(lifetime.IsCurrent(replacement));
        Assert.True(lifetime.IsCurrent(null));
    }

    [Fact]
    public void Replacing_and_transferring_a_capture_preserves_the_bounded_owner_contract()
    {
        var world = new FakeWorldContext();
        var lifetime = new TerrainLodSourceLifetime(world.ChunkHost.GetChunk(0, 0));
        using var captures = new TerrainLodVisualCaptures(1);
        captures.Replace((0, 0), new(lifetime, Snapshot(world)));
        var next = new TerrainLodVisualCaptures.Capture(lifetime, Snapshot(world));
        captures.Replace((0, 0), next);
        Assert.Equal(1, captures.Count);
        using var rejected = Snapshot(world);
        Assert.Throws<InvalidOperationException>(() => captures.Replace((1, 0), new(lifetime, rejected)));
        Assert.Equal(1, captures.Count);
        captures.TransferToCompiler((0, 0));
        Assert.Equal(0, captures.Count);
        Assert.Equal(0, captures.RetainedArrayBytes);
        captures.Dispose();
        // Ownership has moved; store teardown must not return these arrays a second time.
        next.Visuals.Dispose();
        captures.Dispose();
    }

    private static WorldRegionSnapshot Snapshot(FakeWorldContext world) =>
        new(world, 0, 0, 0, 15, ChuckFormat.WorldHeight - 1, 15);
}
