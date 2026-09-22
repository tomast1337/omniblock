using System.Text.Json;
using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Network.Messages;
using OmniBlock.Server.Worlds;
using OmniBlock.Worlds.Lod;
using Xunit.Abstractions;

namespace OmniBlock.Tests.Worlds;

public sealed partial class InactiveGenerationWorkspaceTests
{
    private readonly ITestOutputHelper _output;

    public InactiveGenerationWorkspaceTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    public void TerrainLod_generated_near_detail_separates_source_loss_from_presentation_loss(int tileX, int tileZ)
    {
        // Actual generation, decoration and lighting, never a fabricated cave or uniform tile.
        // This is a CPU fidelity/cost comparison, not a screenshot or frame-time benchmark.
        var world = new SourceWorld(246813579L);
        var key = new TerrainLodTileKey(TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel, tileX, tileZ);
        Assert.Equal(2, key.Level); // This modest fixture intentionally generates only 4x4 chunks.
        var batch = new InactiveGenerationWorkspace(world)
            .GenerateCompletedNeighborhood(checked((int)key.MinChunkX + 1), checked((int)key.MinChunkZ + 1));
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var policy = TerrainLodSpatialPolicy.CreateDefault();
        var children = Enumerable.Range(0, 4).Select(i =>
        {
            var child = key.Child(i);
            var leaves = Enumerable.Range(0, 4).Select(j =>
            {
                var leaf = child.Child(j);
                return TerrainLodColumnTile.BuildLeaf(batch.Get(leaf.X, leaf.Z).CaptureTerrain(), materials);
            }).ToArray();
            return TerrainLodColumnTile.BuildParent(child, leaves, horizontalSampleLevel: 0);
        }).ToArray();
        var exact = TerrainLodColumnTile.BuildParent(key, children, horizontalSampleLevel: 0);
        var shipped = TerrainLodColumnTile.BuildParent(key, children,
            policy.HorizontalSampleLevelForSpatialLevel(key.Level));
        var hash = exact.CanonicalHash;

        var current = Measure("shipped", shipped, policy.VerticalSliceBudgetForSpatialLevel(key.Level), 60);
        var fine = Measure("retained-1x1-same-presentation", exact,
            policy.VerticalSliceBudgetForSpatialLevel(key.Level), 60);
        var reference = Measure("retained-1x1-unreduced-reference", exact, 128, null);

        Assert.True(reference.CaveAirVoxels > 0, "Fixture contains no air below an opaque roof");
        Assert.True(reference.SkylitCaveAirVoxels > 0, "Fixture contains no skylit cave/overhang air");
        Assert.Equal(0, fine.CanonicalMaterialMismatches);
        Assert.Equal(0, fine.PresentedClosedSkylitCaveAir);
        Assert.Equal(0, reference.PresentedMaterialMismatches);
        Assert.Equal(0, reference.PresentedClosedCaveAir);
        Assert.Equal(hash, exact.CanonicalHash); // Rendering never changes durable source data.
        if (shipped.HorizontalSampleLevel > 0)
            Assert.True(current.CanonicalMaterialMismatches > 0, "Fixture did not exercise horizontal reduction");

        Fidelity Measure(string name, TerrainLodColumnTile source, int verticalBudget, int? caveCeiling)
        {
            // Exercise compressed transport too, not only the integrated-server shortcut.
            var message = TerrainLodTileMessage.Of(world.Dimension.Id, source);
            var decoded = message.Decode();
            Assert.Equal(source.CanonicalHash, decoded.CanonicalHash);
            var columns = new TerrainLodColumn[decoded.Width, decoded.Width];
            for (var x = 0; x < decoded.Width; x++)
            for (var z = 0; z < decoded.Width; z++)
            {
                var canonical = decoded[x, z];
                columns[x, z] = TerrainLodVerticalSliceReducer.Reduce(caveCeiling is { } ceiling
                    ? TerrainLodCaveCuller.SealUndergroundAir(canonical, ceiling) : canonical, verticalBudget);
            }

            long canonicalMismatch = 0, presentedMismatch = 0, caveAir = 0, skylitCaveAir = 0;
            long canonicalClosed = 0, presentedClosed = 0, canonicalSkylitClosed = 0, presentedSkylitClosed = 0;
            (long X, int Y, long Z)? firstOpening = null;
            for (var x = 0; x < exact.Width; x++)
            for (var z = 0; z < exact.Width; z++)
            {
                var roof = false;
                var canonical = decoded[x >> decoded.HorizontalSampleLevel, z >> decoded.HorizontalSampleLevel];
                var presented = columns[x >> decoded.HorizontalSampleLevel, z >> decoded.HorizontalSampleLevel];
                for (var y = exact[x, z].WorldHeight - 1; y >= 0; y--)
                {
                    var expected = exact[x, z].At(y);
                    var before = canonical.At(y);
                    var after = presented.At(y);
                    if (expected.Material != before.Material) canonicalMismatch++;
                    if (expected.Material != after.Material) presentedMismatch++;
                    if (expected.Material.OccludesFaces) roof = true;
                    if (!roof || !expected.IsAir) continue;
                    caveAir++;
                    if (!before.IsAir) canonicalClosed++;
                    if (!after.IsAir) presentedClosed++;
                    if (expected.SkyLight == 0) continue;
                    skylitCaveAir++;
                    firstOpening ??= (key.MinChunkX * 16 + x, y, key.MinChunkZ * 16 + z);
                    if (!before.IsAir) canonicalSkylitClosed++;
                    if (!after.IsAir) presentedSkylitClosed++;
                }
            }

            var mesh = TerrainLodSpatialMeshBuilder.Build(decoded, world.Content.Blocks, verticalBudget,
                emitTileBoundaryFaces: false, caveCullBelowY: caveCeiling);
            var quality = TerrainLodSpatialMeshQuality.FromMesh(mesh);
            var result = new Fidelity(caveAir, skylitCaveAir, canonicalMismatch, presentedMismatch,
                canonicalClosed, presentedClosed, canonicalSkylitClosed, presentedSkylitClosed);
            _output.WriteLine(JsonSerializer.Serialize(new
            {
                Fixture = name, Seed = 246813579L, Tile = key,
                FirstSkylitAirUnderRoof = firstOpening?.ToString(),
                Fidelity = result, Mesh = quality, mesh.EstimatedBytes,
                CompressedBytes = message.Compressed.Length
            }));
            return result;
        }
    }

    private sealed record Fidelity(
        long CaveAirVoxels, long SkylitCaveAirVoxels,
        long CanonicalMaterialMismatches, long PresentedMaterialMismatches,
        long CanonicalClosedCaveAir, long PresentedClosedCaveAir,
        long CanonicalClosedSkylitCaveAir, long PresentedClosedSkylitCaveAir);
}
