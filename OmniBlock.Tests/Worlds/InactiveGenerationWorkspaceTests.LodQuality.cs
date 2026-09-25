using System.Text.Json;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Network.Messages;
using OmniBlock.Server.Worlds;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Lod;
using Xunit.Abstractions;

namespace OmniBlock.Tests.Worlds;

public sealed partial class InactiveGenerationWorkspaceTests
{
    private readonly ITestOutputHelper _output;

    public InactiveGenerationWorkspaceTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Near_cave_tile_has_a_rock_roof_opening()
    {
        var world = new SourceWorld(246813579L);
        var targets = from x in Enumerable.Range(0, 4)
            from z in Enumerable.Range(0, 4)
            select new ChunkPos(x, z);
        var batch = new InactiveGenerationWorkspace(world).GenerateCompletedRegion(targets);
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var key = new TerrainLodTileKey(2, 0, 0);
        var children = Enumerable.Range(0, 4).Select(index =>
        {
            var child = key.Child(index);
            var leaves = Enumerable.Range(0, 4).Select(leafIndex =>
            {
                var leaf = child.Child(leafIndex);
                return TerrainLodColumnTile.BuildLeaf(
                    batch.Get(leaf.X, leaf.Z).CaptureTerrain()
                        .WithClimate(world.Dimension.BiomeSource), materials);
            }).ToArray();
            return TerrainLodColumnTile.BuildParent(child, leaves, 0);
        }).ToArray();
        var tile = TerrainLodColumnTile.BuildParent(key, children, 0);
        Assert.Equal("a692f76a803bc99b126d0afd6d4c1be51d7c998f38d1aba748fb669815b9b599",
            tile.CanonicalHash);
        var column = tile[24, 33];
        var opening = column.At(76);
        Assert.True(opening.IsAir);
        Assert.Equal(14, opening.SkyLight);
        Assert.Contains(Enumerable.Range(77, 8), y =>
            column.At(y).Material.BlockId.ToString() == "omniblock:stone");
        var exposure = TerrainLodCaveCuller.GetDefaultExposure(tile);
        var presented = TerrainLodVerticalSliceReducer.Reduce(
            TerrainLodCaveCuller.SealUndergroundAir(column, 60,
                exposure.ForColumn(24, 33)), 16);
        Assert.True(presented.At(76).IsAir);
    }

    [Theory]
    [InlineData("GLACIER", 826164623L,
        "e944cf608890d3e5c31fc60ddc509501645ce94e88467f192b6a2ef885b8887c",
        995L, 170L, 69, 83)]
    [InlineData("GARGAMEL", -841147678L,
        "18be3a8d86212fe1bb9ff30d64e81d320c6d3ab6d2cda4e774e25dc09f50d97c",
        13927L, 1L, 58, 71)]
    public void Historical_seed_completed_neighborhood_has_a_stable_lod_source(
        string name, long seed, string expectedHash,
        long expectedCaveAir, long expectedSkylitCaveAir,
        int expectedMinimumSurface, int expectedMaximumSurface)
    {
        // Use the completed-neighborhood path so decoration order and its dependency halo
        // match the source used by real distant terrain, not a bare chunk-noise preview.
        var world = new SourceWorld(seed);
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var key = new TerrainLodTileKey(2, 0, 0);
        TerrainLodColumnTile BuildTile()
        {
            var batch = new InactiveGenerationWorkspace(world).GenerateCompletedNeighborhood(1, 1);
            var children = Enumerable.Range(0, 4).Select(index =>
            {
                var child = key.Child(index);
                var leaves = Enumerable.Range(0, 4).Select(leafIndex =>
                {
                    var leaf = child.Child(leafIndex);
                    return TerrainLodColumnTile.BuildLeaf(
                        batch.Get(leaf.X, leaf.Z).CaptureTerrain()
                            .WithClimate(world.Dimension.BiomeSource), materials);
                }).ToArray();
                return TerrainLodColumnTile.BuildParent(child, leaves, horizontalSampleLevel: 0);
            }).ToArray();
            return TerrainLodColumnTile.BuildParent(key, children, horizontalSampleLevel: 0);
        }

        var tile = BuildTile();
        Assert.Equal(tile.CanonicalHash, BuildTile().CanonicalHash);
        var caveAir = 0L;
        var skylitCaveAir = 0L;
        var minimumSurface = int.MaxValue;
        var maximumSurface = int.MinValue;
        for (var x = 0; x < tile.Width; x++)
        for (var z = 0; z < tile.Width; z++)
        {
            var spans = tile[x, z].Spans;
            var roof = false;
            for (var index = spans.Count - 1; index >= 0; index--)
            {
                var span = spans[index];
                if (span.Material.OccludesFaces)
                {
                    if (!roof)
                    {
                        minimumSurface = Math.Min(minimumSurface, span.TopY);
                        maximumSurface = Math.Max(maximumSurface, span.TopY);
                    }
                    roof = true;
                }
                else if (roof && span.IsAir)
                {
                    caveAir += span.Height;
                    if (span.SkyLight != 0) skylitCaveAir += span.Height;
                }
            }
        }
        _output.WriteLine($"{name} seed={seed} tile={key} hash={tile.CanonicalHash} " +
                          $"caveAir={caveAir} skylitCaveAir={skylitCaveAir} " +
                          $"surfaceRange={minimumSurface}..{maximumSurface}");
        Assert.Equal(expectedHash, tile.CanonicalHash);
        Assert.Equal(expectedCaveAir, caveAir);
        Assert.Equal(expectedSkylitCaveAir, skylitCaveAir);
        Assert.Equal(expectedMinimumSurface, minimumSurface);
        Assert.Equal(expectedMaximumSurface, maximumSurface);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(8, 0)]
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
        if (tileX == 0)
        {
            AssertLocalStoneFaceCoverage(batch.Get(1, 2).CaptureTerrain(), materials, world);
            AssertGeneratedCaveBoundaryFace(
                batch.Get(1, 1).CaptureTerrain(), batch.Get(1, 2).CaptureTerrain(),
                materials, world);
        }
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
        var withoutCaveMouths = Measure("without-cave-mouth-exposure", shipped,
            policy.VerticalSliceBudgetForSpatialLevel(key.Level), 60, preserveCaveMouths: false);
        var withoutCaveCull = Measure("16-span-without-cave-cull", shipped,
            policy.VerticalSliceBudgetForSpatialLevel(key.Level), null);
        var withoutSpanReduction = Measure("32-span-with-cave-cull", shipped, 32, 60);
        // Keep an explicit policy-v1 counterfactual so the historical loss remains measurable
        // after the shipped policy adopts 1x1. This never enters the live cache/runtime.
        var legacy = Measure("legacy-v1-2x2", TerrainLodColumnTile.BuildParent(key, children, 1),
            policy.VerticalSliceBudgetForSpatialLevel(key.Level), 60);
        var reference = Measure("retained-1x1-unreduced-reference", exact, 128, null);

        Assert.True(reference.CaveAirVoxels > 0, "Fixture contains no air below an opaque roof");
        Assert.True(reference.SkylitCaveAirVoxels > 0, "Fixture contains no skylit cave/overhang air");
        if (tileX == 0)
            Assert.Equal((24L, 76, 33L), reference.FirstSkylitAirUnderRoof);
        Assert.Equal(0, shipped.HorizontalSampleLevel);
        Assert.Equal(64, shipped.Width);
        Assert.Equal(0, current.CanonicalMaterialMismatches);
        Assert.Equal(0, current.PresentedClosedSkylitCaveAir);
        Assert.True(withoutCaveCull.PresentedClosedCaveAir <= current.PresentedClosedCaveAir);
        Assert.True(withoutSpanReduction.PresentedClosedCaveAir <= current.PresentedClosedCaveAir);
        Assert.True(current.PresentedClosedCaveAir < withoutCaveMouths.PresentedClosedCaveAir,
            "The generated cave mouth should preserve dark air the old sealing policy lost.");
        Assert.True(current.MeshBytes <= TerrainLodScaleBudget.MaximumUploadBytesPerFrame);
        Assert.True(current.PresentedClosedCaveAir - withoutCaveCull.PresentedClosedCaveAir >
                    current.PresentedClosedCaveAir - withoutSpanReduction.PresentedClosedCaveAir,
            "Cave sealing should be the dominant loss in this generated near-detail fixture.");
        Assert.True(legacy.CanonicalMaterialMismatches > 0, "Fixture did not exercise horizontal reduction");
        Assert.Equal(0, reference.PresentedMaterialMismatches);
        Assert.Equal(0, reference.PresentedClosedCaveAir);
        Assert.Equal(hash, exact.CanonicalHash); // Rendering never changes durable source data.

        Fidelity Measure(string name, TerrainLodColumnTile source, int verticalBudget,
            int? caveCeiling, bool preserveCaveMouths = true)
        {
            // Exercise compressed transport too, not only the integrated-server shortcut.
            var message = TerrainLodTileMessage.Of(world.Dimension.Id, source);
            var decoded = message.Decode();
            Assert.Equal(source.CanonicalHash, decoded.CanonicalHash);
            var columns = new TerrainLodColumn[decoded.Width, decoded.Width];
            var exposure = preserveCaveMouths && caveCeiling is not null &&
                           decoded.HorizontalSampleLevel == 0
                ? TerrainLodCaveCuller.GetDefaultExposure(decoded)
                : null;
            for (var x = 0; x < decoded.Width; x++)
            for (var z = 0; z < decoded.Width; z++)
            {
                var canonical = decoded[x, z];
                columns[x, z] = TerrainLodVerticalSliceReducer.Reduce(caveCeiling is { } ceiling
                    ? TerrainLodCaveCuller.SealUndergroundAir(canonical, ceiling,
                        exposure is null ? [] : exposure.ForColumn(x, z))
                    : canonical, verticalBudget);
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

            // The legacy sealing counterfactual has no live mesh policy; measure its voxel loss
            // only, while compiling every shipped candidate through the actual builder.
            var mesh = preserveCaveMouths
                ? TerrainLodSpatialMeshBuilder.Build(decoded, world.Content.Blocks, verticalBudget,
                    emitTileBoundaryFaces: false, caveCullBelowY: caveCeiling,
                    maximumResultBytes: TerrainLodScaleBudget.MaximumUploadBytesPerFrame)
                : null;
            if (mesh is not null)
                Assert.InRange(mesh.EstimatedBytes, 1, TerrainLodScaleBudget.MaximumUploadBytesPerFrame);
            Assert.InRange(message.Compressed.Length, 1, TerrainLodScaleBudget.MaximumCompressedTileBytes);
            TerrainLodSpatialMeshQuality? quality = mesh is null
                ? null : TerrainLodSpatialMeshQuality.FromMesh(mesh);
            var result = new Fidelity(caveAir, skylitCaveAir, canonicalMismatch, presentedMismatch,
                canonicalClosed, presentedClosed, canonicalSkylitClosed, presentedSkylitClosed,
                firstOpening, mesh?.EstimatedBytes ?? 0);
            _output.WriteLine(JsonSerializer.Serialize(new
            {
                Fixture = name, Seed = 246813579L, Tile = key,
                FirstSkylitAirUnderRoof = firstOpening?.ToString(),
                Fidelity = result, Mesh = quality, EstimatedBytes = mesh?.EstimatedBytes,
                CompressedBytes = message.Compressed.Length
            }));
            return result;
        }
    }

    private sealed record Fidelity(
        long CaveAirVoxels, long SkylitCaveAirVoxels,
        long CanonicalMaterialMismatches, long PresentedMaterialMismatches,
        long CanonicalClosedCaveAir, long PresentedClosedCaveAir,
        long CanonicalClosedSkylitCaveAir, long PresentedClosedSkylitCaveAir,
        (long X, int Y, long Z)? FirstSkylitAirUnderRoof, long MeshBytes);

    private static void AssertLocalStoneFaceCoverage(
        TerrainLodSourceSnapshot source,
        TerrainLodMaterialCatalog materials,
        SourceWorld world)
    {
        var hierarchy = TerrainLodReducer.Build(source, materials,
            TerrainLodReductionStrategy.SurfacePreserving);
        var leaf = hierarchy.Levels[0];
        var mesh = TerrainLodMeshBuilder.Build(hierarchy, 0, world.Content.Blocks, true);
        var stone = materials.Resolve(world.Content.Blocks.Get("omniblock:stone").Id, 0).BlockId;
        TerrainLodFaceMask[] faces =
        [
            TerrainLodFaceMask.Down, TerrainLodFaceMask.Up,
            TerrainLodFaceMask.North, TerrainLodFaceMask.South,
            TerrainLodFaceMask.West, TerrainLodFaceMask.East
        ];
        HashSet<(int X, int Y, int Z, TerrainLodFaceMask Face)> emitted = [];
        for (var index = 0; index < mesh.Vertices.Length; index += 4)
        {
            var a = Position(mesh.Vertices[index]);
            var b = Position(mesh.Vertices[index + 1]);
            var c = Position(mesh.Vertices[index + 2]);
            var d = Position(mesh.Vertices[index + 3]);
            var normalX = (b.Y - a.Y) * (c.Z - a.Z) - (b.Z - a.Z) * (c.Y - a.Y);
            var normalY = (b.Z - a.Z) * (c.X - a.X) - (b.X - a.X) * (c.Z - a.Z);
            var normalZ = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            if (a.X == b.X && a.X == c.X && a.X == d.X)
            {
                var face = normalX > 0 ? TerrainLodFaceMask.East : TerrainLodFaceMask.West;
                emitted.Add((a.X - (normalX > 0 ? 1 : 0),
                    Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)),
                    Math.Min(Math.Min(a.Z, b.Z), Math.Min(c.Z, d.Z)), face));
            }
            else if (a.Y == b.Y && a.Y == c.Y && a.Y == d.Y)
            {
                var face = normalY > 0 ? TerrainLodFaceMask.Up : TerrainLodFaceMask.Down;
                emitted.Add((Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)),
                    a.Y - (normalY > 0 ? 1 : 0),
                    Math.Min(Math.Min(a.Z, b.Z), Math.Min(c.Z, d.Z)), face));
            }
            else if (a.Z == b.Z && a.Z == c.Z && a.Z == d.Z)
            {
                var face = normalZ > 0 ? TerrainLodFaceMask.South : TerrainLodFaceMask.North;
                emitted.Add((Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)),
                    Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)),
                    a.Z - (normalZ > 0 ? 1 : 0), face));
            }
        }

        var checkedFaces = 0;
        for (var x = 1; x < 15; x++)
        for (var z = 1; z < 15; z++)
        for (var y = 60; y < source.Height - 1; y++)
        {
            var cell = leaf[x, y, z];
            if (cell.IsEmpty || cell.Primary.BlockId != stone) continue;
            foreach (var face in faces)
            {
                if ((cell.ExposedFaces & face) == 0) continue;
                Assert.Contains((x, y, z, face), emitted);
                checkedFaces++;
            }
        }
        Assert.True(checkedFaces > 100, "Generated cave fixture did not test enough stone faces.");

        static (int X, int Y, int Z) Position(ChunkVertex vertex) =>
            ((int)MathF.Round(vertex.X * 64f / 32767f),
                (int)MathF.Round(vertex.Y * 64f / 32767f + ChuckFormat.WorldHeight / 2f),
                (int)MathF.Round(vertex.Z * 64f / 32767f));
    }

    private static void AssertGeneratedCaveBoundaryFace(
        TerrainLodSourceSnapshot northSource,
        TerrainLodSourceSnapshot southSource,
        TerrainLodMaterialCatalog materials,
        SourceWorld world)
    {
        var north = TerrainLodReducer.Build(northSource, materials,
            TerrainLodReductionStrategy.SurfacePreserving);
        var south = TerrainLodReducer.Build(southSource, materials,
            TerrainLodReductionStrategy.SurfacePreserving);
        var owner = TerrainLodBoundarySummary.Capture(north, 0, 4);
        var neighbor = TerrainLodBoundarySummary.Capture(south, 0, 4);
        var stone = materials.Resolve(world.Content.Blocks.Get("omniblock:stone").Id, 0).BlockId;
        Assert.True(north.Levels[0][13, 76, 15].IsEmpty);
        Assert.Equal(stone, south.Levels[0][13, 76, 0].Primary.BlockId);

        var seam = TerrainLodSeamMeshBuilder.BuildSolid(
            owner, 0, neighbor, 0, OmniBlock.Blocks.Side.South,
            world.Content.Blocks, true,
            materialSide: TerrainLodSeamMaterialSide.Neighbor);
        var face = false;
        for (var index = 0; index < seam.Vertices.Length; index += 4)
        {
            var vertices = seam.Vertices.AsSpan(index, 4);
            var minimumX = int.MaxValue;
            var maximumX = int.MinValue;
            var minimumY = int.MaxValue;
            var maximumY = int.MinValue;
            var onBoundary = true;
            foreach (var vertex in vertices)
            {
                onBoundary &= (int)MathF.Round(vertex.Z * 64f / 32767f) == 16;
                var x = (int)MathF.Round(vertex.X * 64f / 32767f);
                var y = (int)MathF.Round(vertex.Y * 64f / 32767f + ChuckFormat.WorldHeight / 2f);
                minimumX = Math.Min(minimumX, x);
                maximumX = Math.Max(maximumX, x);
                minimumY = Math.Min(minimumY, y);
                maximumY = Math.Max(maximumY, y);
            }
            if (onBoundary && minimumX == 13 && maximumX == 14 &&
                minimumY == 76 && maximumY == 77)
            {
                face = true;
                break;
            }
        }
        Assert.True(face, "Generated local L0 seam omitted the cave's north-facing stone face.");
    }
}
