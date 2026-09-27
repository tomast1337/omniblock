using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Network.Messages;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Lod;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class ClientTerrainLodSpatialCacheTests
{
    [Fact]
    public async Task Server_scoped_tile_is_available_after_reopening_without_render_thread_io()
    {
        var root = new DirectoryInfo(Path.Combine(
            Path.GetTempPath(), $"omniblock-client-spatial-{Guid.NewGuid():N}"));
        var identity = new TerrainLodCacheIdentity(
            "world", 0, "content", "generator",
            TerrainLodHierarchy.ReductionSchemaVersion, "materials", 6,
            TerrainLodSpatialPolicy.CurrentQualityPolicyVersion);
        var key = new TerrainLodTileKey(2, -3, 4);
        var column = TerrainLodColumn.Create(4,
        [new TerrainLodColumnSpan(0, 4, TerrainLodMaterial.Air, 0, 15)]);
        var tile = TerrainLodColumnTile.CreateUniform(key, 0, 4, column, "fixture");
        try
        {
            using (var first = new ClientTerrainLodSpatialCache(root, identity))
                first.QueueWrite(tile);
            using var reopened = new ClientTerrainLodSpatialCache(root, identity);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            ClientTerrainLodCacheProbe result;
            string hash;
            do
            {
                result = reopened.Probe(key, out hash);
                if (result != ClientTerrainLodCacheProbe.Pending) break;
                await Task.Delay(5);
            } while (DateTime.UtcNow < deadline);

            Assert.Equal(ClientTerrainLodCacheProbe.Available, result);
            Assert.Equal(tile.CanonicalHash, hash);
            Assert.True(reopened.TryGetValidatedTile(key, out var restored));
            Assert.Equal(tile.CanonicalHash, restored!.CanonicalHash);
        }
        finally
        {
            if (root.Exists) root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Different_worlds_keep_independent_cached_tiles()
    {
        var root = new DirectoryInfo(Path.Combine(
            Path.GetTempPath(), $"omniblock-client-spatial-{Guid.NewGuid():N}"));
        var firstIdentity = new TerrainLodCacheIdentity(
            "first-world", 0, "content", "generator",
            TerrainLodHierarchy.ReductionSchemaVersion, "materials", 6,
            TerrainLodSpatialPolicy.CurrentQualityPolicyVersion);
        var secondIdentity = firstIdentity with { WorldFingerprint = "second-world" };
        var key = new TerrainLodTileKey(2, 1, 2);
        var column = TerrainLodColumn.Create(4,
            [new TerrainLodColumnSpan(0, 4, TerrainLodMaterial.Air, 0, 15)]);
        var tile = TerrainLodColumnTile.CreateUniform(key, 0, 4, column, "fixture");
        try
        {
            using (var first = new ClientTerrainLodSpatialCache(root, firstIdentity))
                first.QueueWrite(tile);

            using (var second = new ClientTerrainLodSpatialCache(root, secondIdentity))
                Assert.Equal(ClientTerrainLodCacheProbe.Missing,
                    await ProbeUntilReady(second, key));

            using var reopened = new ClientTerrainLodSpatialCache(root, firstIdentity);
            Assert.Equal(ClientTerrainLodCacheProbe.Available,
                await ProbeUntilReady(reopened, key));
        }
        finally
        {
            if (root.Exists) root.Delete(recursive: true);
        }
    }

    private static async Task<ClientTerrainLodCacheProbe> ProbeUntilReady(
        ClientTerrainLodSpatialCache cache, TerrainLodTileKey key)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        ClientTerrainLodCacheProbe result;
        do
        {
            result = cache.Probe(key, out _);
            if (result != ClientTerrainLodCacheProbe.Pending) return result;
            await Task.Delay(5);
        } while (DateTime.UtcNow < deadline);
        return result;
    }

    [Fact]
    public void Cached_tile_is_not_published_until_server_confirms_its_hash()
    {
        using var probe = new ClientTerrainLodRenderer(new LightTestWorld());
        var camera = new Vector3D<double>(0, 80, 0);
        var key = probe.TakeRemoteSpatialRequests(camera, 0, 4, 1)[0];
        var column = TerrainLodColumn.Create(4,
        [new TerrainLodColumnSpan(0, 4, TerrainLodMaterial.Air, 0, 15)]);
        var tile = TerrainLodColumnTile.CreateUniform(key, 0, 4, column, "validated-fixture");
        var root = new DirectoryInfo(Path.Combine(
            Path.GetTempPath(), $"omniblock-client-spatial-{Guid.NewGuid():N}"));
        var identity = new TerrainLodCacheIdentity(
            "world", 0, "content", "generator",
            TerrainLodHierarchy.ReductionSchemaVersion, "materials", 6,
            TerrainLodSpatialPolicy.CurrentQualityPolicyVersion);
        try
        {
            new TerrainLodColumnTileCacheStore(
                new DirectoryInfo(Path.Combine(root.FullName, identity.RecordFingerprint)),
                identity).Write(tile);
            using var renderer = new ClientTerrainLodRenderer(new LightTestWorld());
            renderer.ConfigureRemoteSpatialCache(identity, root);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            var offered = false;
            while (DateTime.UtcNow < deadline && !offered)
            {
                foreach (var requested in renderer.TakeRemoteSpatialRequests(camera, 0, 4, 16))
                    if (requested == key && renderer.CachedRemoteHash(key) == tile.CanonicalHash)
                        offered = true;
                if (!offered) Thread.Sleep(5);
            }

            Assert.True(offered);
            Assert.Null(renderer.GetResidentSpatialSourceHash(key));
            renderer.ObserveRemoteSpatialStatus(key, TerrainLodTileStatus.NotModified, 1);
            Assert.Equal(tile.CanonicalHash, renderer.GetResidentSpatialSourceHash(key));
        }
        finally
        {
            if (root.Exists) root.Delete(recursive: true);
        }
    }
}
