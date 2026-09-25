using System.Diagnostics;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Registries;
using OmniBlock.Textures;
using OmniBlock.Worlds.ClientData.Colors;
using OmniBlock.Worlds.Colors;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

/// <summary>
///     One independently uploadable part of a spatial LOD tile. Pages deliberately remain within
///     the packed chunk vertex's 64-block position range; their world origin is supplied through
///     the ordinary chunk draw metadata at presentation time.
/// </summary>
internal sealed record TerrainLodSpatialMeshPage(
    TerrainLodSpatialMeshPageKey Key,
    int OriginX,
    int OriginY,
    int OriginZ,
    int ExtentX,
    int ExtentY,
    int ExtentZ,
    ChunkVertex[] Vertices,
    ChunkLightVertex[] Lights,
    ChunkDirectionalRanges SolidRanges,
    ChunkVertex[] TranslucentVertices,
    ChunkLightVertex[] TranslucentLights,
    ChunkDirectionalRanges TranslucentRanges)
{
    public long EstimatedBytes =>
        (long)Vertices.Length * WgpuMesh.ChunkVertexStride +
        (long)Lights.Length * WgpuMesh.ChunkLightVertexStride +
        (long)TranslucentVertices.Length * WgpuMesh.ChunkVertexStride +
        (long)TranslucentLights.Length * WgpuMesh.ChunkLightVertexStride;
}

internal readonly record struct TerrainLodSpatialMeshPageKey(int X, int Y, int Z);

internal readonly record struct TerrainLodQuadLighting(
    ChunkLightVertex A,
    ChunkLightVertex B,
    ChunkLightVertex C,
    ChunkLightVertex D)
{
    public static TerrainLodQuadLighting Uniform(ChunkLightVertex light) =>
        new(light, light, light, light);
}

internal readonly record struct TerrainLodSpatialMeshBuildProfile(
    double ReductionMs,
    double FaceEmissionMs,
    double FlatteningMs,
    double CoalescingMs,
    int SourceColumns,
    int SourceSpans,
    int ConstructionPages,
    int CanonicalSpans = 0,
    int CaveCulledColumns = 0,
    int VerticalReducedColumns = 0,
    int RenderedSpans = 0);

/// <summary>Immutable CPU result for one canonical spatial column tile.</summary>
internal sealed record TerrainLodSpatialMeshData(
    TerrainLodTileKey Key,
    string CanonicalHash,
    int HorizontalSampleLevel,
    int VerticalSliceBudget,
    bool IncludesExternalBoundaryFaces,
    int MaximumRenderedSpans,
    TerrainLodSpatialMeshPage[] Pages,
    TerrainLodSpatialMeshBuildProfile Profile)
{
    public int? CaveCullBelowY { get; init; }
    public long EstimatedBytes => Pages.Sum(static page => page.EstimatedBytes);
    public int[] ArenaAllocationVertexCounts => Pages
        .SelectMany(static page => new[]
        {
            page.Vertices.Length,
            page.TranslucentVertices.Length
        })
        .Where(static count => count > 0)
        .ToArray();
    public int SolidQuadCount => Pages.Sum(static page => page.Vertices.Length / 4);
    public int TranslucentQuadCount => Pages.Sum(static page =>
        page.TranslucentVertices.Length / 4);
}

/// <summary>
///     Converts the persistent column-span hierarchy into packed terrain quad streams. The
///     canonical tile is never changed: vertical quality is applied to private presentation
///     columns, then exposed intervals are emitted into deterministic 64-cubed pages.
/// </summary>
internal static class TerrainLodSpatialMeshBuilder
{
    // ChunkVertex uses 32767 / 64 fixed-point positions. Vertical runs remain split at sixteen
    // blocks; a coarse horizontal sample may span one complete 64-block page and carries a packed
    // power-of-two UV scale so its texture still repeats once per block.
    internal const int PageSize = 64;
    internal const int MaximumQuadSpan = 16;
    internal const int MaximumSampleSpan = PageSize;
    // L5 is the first policy tier whose selected nodes are wholly distant from the exact/LOD
    // handoff and whose 64-block construction pages otherwise multiply draw count materially.
    // L2-L4 retain individual pages so the shader's 6x6 per-page authority mask can hide exact
    // columns during near-boundary handoff.
    internal const int TileScaleSubmissionMinimumLevel = 5;

    public static TerrainLodSpatialMeshData Build(
        TerrainLodColumnTile tile,
        IBlockRuntimeView blocks,
        int verticalSliceBudget,
        bool emitTileBoundaryFaces = true,
        int? caveCullBelowY = null,
        CancellationToken cancellationToken = default,
        long maximumResultBytes = long.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(blocks);
        if (verticalSliceBudget <= 0)
            throw new ArgumentOutOfRangeException(nameof(verticalSliceBudget));
        var guard = new TerrainLodSpatialMeshBuildGuard(
            cancellationToken, maximumResultBytes);

        var sampleSize = checked(1 << tile.HorizontalSampleLevel);
        if (sampleSize > MaximumSampleSpan)
            throw new NotSupportedException(
                $"Spatial terrain sample size {sampleSize} exceeds the packed position-page " +
                $"limit of {MaximumSampleSpan} blocks.");
        var expectedFootprint = checked((long)tile.Key.ChunkWidth * 16);
        if ((long)tile.Width * sampleSize != expectedFootprint)
            throw new InvalidDataException(
                $"Spatial terrain tile {tile.Key} covers {tile.Width * sampleSize} blocks; " +
                $"expected {expectedFootprint}.");

        var tileMinX = checked(tile.Key.MinChunkX * 16);
        var tileMinZ = checked(tile.Key.MinChunkZ * 16);
        if (tileMinX is < int.MinValue or > int.MaxValue ||
            tileMinZ is < int.MinValue or > int.MaxValue ||
            tileMinX + expectedFootprint > int.MaxValue ||
            tileMinZ + expectedFootprint > int.MaxValue)
            throw new InvalidDataException($"Spatial terrain tile {tile.Key} is outside renderable coordinates.");

        var stageStarted = Stopwatch.GetTimestamp();
        var columns = new TerrainLodColumn[checked(tile.Width * tile.Width)];
        var maximumRenderedSpans = 0;
        var sourceSpans = 0;
        var canonicalSpans = 0;
        var caveCulledColumns = 0;
        var verticalReducedColumns = 0;
        var renderedSpans = 0;
        var caveExposure = caveCullBelowY is not null && sampleSize == 1
            ? TerrainLodCaveCuller.GetDefaultExposure(tile, cancellationToken)
            : null;
        for (var x = 0; x < tile.Width; x++)
        for (var z = 0; z < tile.Width; z++)
        {
            guard.Checkpoint();
            var source = caveCullBelowY is { } ceilingY
                ? TerrainLodCaveCuller.SealUndergroundAir(
                    tile[x, z], ceilingY,
                    caveExposure is null ? [] : caveExposure.ForColumn(x, z))
                : tile[x, z];
            var reduced = TerrainLodVerticalSliceReducer.Reduce(source, verticalSliceBudget);
            // Constant-time evidence from the actual build, not a second fidelity scan. These
            // counters distinguish presentation simplification from already-reduced source data.
            canonicalSpans += tile[x, z].Spans.Count;
            if (!ReferenceEquals(source, tile[x, z])) caveCulledColumns++;
            if (!ReferenceEquals(reduced, source)) verticalReducedColumns++;
            renderedSpans += reduced.Spans.Count;
            columns[x * tile.Width + z] = reduced;
            sourceSpans += source.Spans.Count;
            maximumRenderedSpans = Math.Max(maximumRenderedSpans, reduced.Spans.Count);
        }
        var reductionMs = Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds;

        Dictionary<TerrainLodSpatialMeshPageKey, PageBuilder> pages = [];
        blocks.TryGet("omniblock:grass_block", out var grassBlock);
        blocks.TryGet("omniblock:leaves", out var leavesBlock);
        blocks.TryGet("omniblock:grass", out var tallGrassBlock);
        var grassOverlayTexture = grassBlock is null
            ? -1
            : Atlases.Terrain.IndexOf("omniblock:grass_block_side_overlay");
        var snowyGrassTexture = grassBlock is null
            ? -1
            : Atlases.Terrain.IndexOf("omniblock:grass_block_side_snowy");

        stageStarted = Stopwatch.GetTimestamp();
        for (var x = 0; x < tile.Width; x++)
        for (var z = 0; z < tile.Width; z++)
        {
            guard.Checkpoint();
            var column = Column(x, z);
            foreach (var span in column.Spans)
            {
                // The first spatial tier has genuine 1x1 samples. Plants have no cube volume:
                // preserve their crossed silhouette here, but leave wider tiers as surface
                // samples so distant vegetation cannot multiply the horizon's draw cost.
                if (span.Material.Geometry == TerrainLodGeometryClass.CrossedQuad)
                {
                    if (sampleSize == 1 &&
                        blocks.TryGet(span.Material.BlockId, out var plant) && plant is not null)
                        EmitCrossedPlant(plant, span);
                    continue;
                }
                if (!TryLayer(span.Material, sampleSize, out var translucent)) continue;
                if (!blocks.TryGet(span.Material.BlockId, out var block) || block is null) continue;

                var minX = checked((int)tileMinX + x * sampleSize);
                var maxX = checked(minX + sampleSize);
                var minZ = checked((int)tileMinZ + z * sampleSize);
                var maxZ = checked(minZ + sampleSize);
                var minY = span.BottomY;
                var maxY = span.TopY;
                var renderMinX = (float)minX;
                var renderMaxX = (float)maxX;
                var renderMinZ = (float)minZ;
                var renderMaxZ = (float)maxZ;
                var renderMinY = (float)minY;
                var renderMaxY = (float)maxY;
                if (sampleSize == 1 && span.Material.Geometry is
                    TerrainLodGeometryClass.SurfaceLayer or TerrainLodGeometryClass.BoundedCube)
                {
                    var bounds = block.DefinitionBoundingBox;
                    renderMinX += (float)bounds.MinX;
                    renderMaxX = minX + (float)bounds.MaxX;
                    renderMinZ += (float)bounds.MinZ;
                    renderMaxZ = minZ + (float)bounds.MaxZ;
                    renderMinY += (float)bounds.MinY;
                    renderMaxY = maxY - 1 + (float)bounds.MaxY;
                    if (block.TerrainLod is { MetadataHeightLevels: > 0 } descriptor)
                        renderMaxY = maxY - 1 +
                            ((span.Material.Metadata & (descriptor.MetadataHeightLevels - 1)) + 1f) /
                            descriptor.MetadataHeightLevels;
                }
                if (span.Material.Geometry == TerrainLodGeometryClass.Liquid &&
                    IsFaceVisible(span.Material, NeighborAt(column, maxY), translucent, blocks))
                    renderMaxY -= FluidMath.GetFluidHeightFromMeta(span.Material.Metadata);

                var below = NeighborAt(column, minY - 1);
                var above = NeighborAt(column, maxY);
                if (IsFaceVisible(span.Material, below, translucent, blocks))
                    EmitHorizontal(Side.Down, renderMinY, anchorY: minY, 0.5f, below);
                if (IsFaceVisible(span.Material, above, translucent, blocks))
                    EmitHorizontal(Side.Up, renderMaxY, anchorY: maxY - 1, 1.0f, above);

                EmitSide(Side.West, x - 1, z, renderMinX, renderMinZ, renderMaxZ, 0.6f);
                EmitSide(Side.East, x + 1, z, renderMaxX, renderMinZ, renderMaxZ, 0.6f);
                EmitSide(Side.North, x, z - 1, renderMinZ, renderMinX, renderMaxX, 0.8f);
                EmitSide(Side.South, x, z + 1, renderMaxZ, renderMinX, renderMaxX, 0.8f);

                void EmitCrossedPlant(Block plant, TerrainLodColumnSpan plantSpan)
                {
                    var worldX = checked((int)tileMinX + x);
                    var worldZ = checked((int)tileMinZ + z);
                    var appearance = Appearance(plant, plantSpan.Material, Side.Down, null, x, z);
                    const float inset = 0.05f;
                    var left = worldX + inset;
                    var right = worldX + 1 - inset;
                    var north = worldZ + inset;
                    var south = worldZ + 1 - inset;
                    for (var plantY = plantSpan.BottomY; plantY < plantSpan.TopY; plantY++)
                    {
                        guard.Checkpoint();
                        var page = PageFor(worldX + 0.5, plantY + 0.5, worldZ + 0.5);
                        var light = FaceLight(plantSpan, NeighborAt(column, plantY + 1), Side.Up);
                        EmitPlane(
                            (left, plantY + 1, north), (left, plantY, north),
                            (right, plantY, south), (right, plantY + 1, south));
                        EmitPlane(
                            (left, plantY + 1, south), (left, plantY, south),
                            (right, plantY, north), (right, plantY + 1, north));

                        void EmitPlane(
                            (float X, float Y, float Z) a,
                            (float X, float Y, float Z) b,
                            (float X, float Y, float Z) c,
                            (float X, float Y, float Z) d)
                        {
                            Emit(page, false, Side.Up, appearance, 1, light, 1, 1,
                                a, b, c, d, guard, nonDirectional: true);
                            Emit(page, false, Side.Up, appearance, 1, light, 1, 1,
                                d, c, b, a, guard, nonDirectional: true);
                        }
                    }
                }

                void EmitHorizontal(
                    Side side,
                    float faceY,
                    int anchorY,
                    float shade,
                    TerrainLodColumnSpan? exposedNeighbor)
                {
                    var faceBlock = block;
                    var faceMaterial = span.Material;
                    if (side == Side.Up && sampleSize > 1 &&
                        exposedNeighbor is { Material.Geometry: TerrainLodGeometryClass.SurfaceLayer } layer &&
                        blocks.TryGet(layer.Material.BlockId, out var layerBlock) &&
                        layerBlock is not null)
                    {
                        // Coarse layers are samples, not enlarged cubes. Use their surface
                        // appearance on the supporting top rather than dropping them entirely.
                        faceBlock = layerBlock;
                        faceMaterial = layer.Material;
                    }
                    var appearance = Appearance(faceBlock, faceMaterial, side, above, x, z);
                    var light = FaceLight(span, exposedNeighbor, side);
                    // sampleSize is bounded by MaximumSampleSpan and the power-of-two sample grid is
                    // aligned to every page boundary, so this quad can never straddle a page.
                    var page = PageFor(
                        minX + sampleSize / 2.0,
                        Math.Clamp(anchorY, 0, tile.WorldHeight - 1) + 0.5,
                        minZ + sampleSize / 2.0);
                    if (side == Side.Up)
                    {
                        var a = (renderMaxX, faceY, renderMaxZ);
                        var b = (renderMaxX, faceY, renderMinZ);
                        var c = (renderMinX, faceY, renderMinZ);
                        var d = (renderMinX, faceY, renderMaxZ);
                        Emit(page, translucent, side, appearance, shade,
                            SmoothLighting(side, span.Material, light, a, b, c, d),
                            sampleSize, sampleSize, a, b, c, d, guard);
                    }
                    else
                    {
                        var a = (renderMinX, faceY, renderMaxZ);
                        var b = (renderMinX, faceY, renderMinZ);
                        var c = (renderMaxX, faceY, renderMinZ);
                        var d = (renderMaxX, faceY, renderMaxZ);
                        Emit(page, translucent, side, appearance, shade,
                            SmoothLighting(side, span.Material, light, a, b, c, d),
                            sampleSize, sampleSize, a, b, c, d, guard);
                    }
                }

                void EmitSide(
                    Side side,
                    int neighborX,
                    int neighborZ,
                    float fixedCoordinate,
                    float alongStart,
                    float alongEnd,
                    float shade)
                {
                    var neighborInBounds = InBounds(neighborX, neighborZ);
                    var insetFace = sampleSize == 1 && IsHorizontallyInsetOnSide(block, side);
                    if (!neighborInBounds && !emitTileBoundaryFaces &&
                        !insetFace) return;
                    var neighbor = neighborInBounds ? Column(neighborX, neighborZ) : null;
                    var runStart = -1.0f;
                    var runLight = default(ChunkLightVertex);

                    // Both columns are canonical vertical interval lists. Walking every block Y
                    // here made face emission O(world height) for every span and every side, then
                    // performed a binary search in the neighbor for each cell. Intersect the
                    // already-reduced intervals directly; output is identical because a run can
                    // change only at a neighbor-span boundary or when its sampled light changes.
                    if (neighbor is null)
                    {
                        var light = FaceLight(span, null, side);
                        EmitVerticalRange(minY, renderMaxY, light);
                    }
                    else
                    {
                        foreach (var adjacentValue in neighbor.Spans)
                        {
                            if (adjacentValue.TopY <= minY) continue;
                            if (adjacentValue.BottomY >= maxY) break;
                            guard.Checkpoint();
                            var rangeBottom = Math.Max(minY, adjacentValue.BottomY);
                            var rangeTop = Math.Min(maxY, adjacentValue.TopY);
                            TerrainLodColumnSpan? adjacent = adjacentValue;
                            var visible = insetFace || IsFaceVisible(
                                span.Material, adjacent, translucent, blocks);
                            var light = FaceLight(span, adjacent, side);
                            if (!visible)
                            {
                                if (runStart >= 0)
                                {
                                    EmitVerticalRange(runStart, rangeBottom, runLight);
                                    runStart = -1;
                                }
                                continue;
                            }
                            if (runStart < 0)
                            {
                                runStart = rangeBottom;
                                runLight = light;
                            }
                            else if (light != runLight)
                            {
                                EmitVerticalRange(runStart, rangeBottom, runLight);
                                runStart = rangeBottom;
                                runLight = light;
                            }

                            if (rangeTop != maxY) continue;
                            EmitVerticalRange(runStart, renderMaxY, runLight);
                            runStart = -1;
                        }
                        if (runStart >= 0)
                        {
                            EmitVerticalRange(runStart, renderMaxY, runLight);
                        }
                    }

                    void EmitVerticalRange(
                        float bottom,
                        float top,
                        ChunkLightVertex light)
                    {
                        bottom = Math.Max(bottom, renderMinY);
                        top = Math.Min(top, renderMaxY);
                        for (var y0 = bottom; y0 < top;)
                        {
                            var pageBoundary = (FloorDivide((int)MathF.Floor(y0), PageSize) + 1) * PageSize;
                            var y1 = Math.Min(top, Math.Min(y0 + MaximumQuadSpan, pageBoundary));
                            if (y1 <= y0) y1 = Math.Min(top, y0 + MaximumQuadSpan);
                            var appearance = Appearance(block, span.Material, side, above, x, z);
                            var anchorX = side is Side.West or Side.East
                                ? (side == Side.West ? fixedCoordinate + 0.001 : fixedCoordinate - 0.001)
                                : (alongStart + alongEnd) * 0.5;
                            var anchorZ = side is Side.North or Side.South
                                ? (side == Side.North ? fixedCoordinate + 0.001 : fixedCoordinate - 0.001)
                                : (alongStart + alongEnd) * 0.5;
                            var page = PageFor(anchorX, (y0 + y1) * 0.5, anchorZ);
                            var height = y1 - y0;
                            switch (side)
                            {
                                case Side.West:
                                    EmitSideQuad(
                                        (fixedCoordinate, y1, alongStart),
                                        (fixedCoordinate, y0, alongStart),
                                        (fixedCoordinate, y0, alongEnd),
                                        (fixedCoordinate, y1, alongEnd));
                                    break;
                                case Side.East:
                                    EmitSideQuad(
                                        (fixedCoordinate, y1, alongEnd),
                                        (fixedCoordinate, y0, alongEnd),
                                        (fixedCoordinate, y0, alongStart),
                                        (fixedCoordinate, y1, alongStart));
                                    break;
                                case Side.North:
                                    EmitSideQuad(
                                        (alongEnd, y1, fixedCoordinate),
                                        (alongEnd, y0, fixedCoordinate),
                                        (alongStart, y0, fixedCoordinate),
                                        (alongStart, y1, fixedCoordinate));
                                    break;
                                case Side.South:
                                    EmitSideQuad(
                                        (alongStart, y1, fixedCoordinate),
                                        (alongStart, y0, fixedCoordinate),
                                        (alongEnd, y0, fixedCoordinate),
                                        (alongEnd, y1, fixedCoordinate));
                                    break;
                            }
                            y0 = y1;

                            void EmitSideQuad(
                                (float X, float Y, float Z) a,
                                (float X, float Y, float Z) b,
                                (float X, float Y, float Z) c,
                                (float X, float Y, float Z) d) =>
                                Emit(page, translucent, side, appearance, shade,
                                    SmoothLighting(side, span.Material, light, a, b, c, d),
                                    alongEnd - alongStart, height, a, b, c, d, guard);
                        }
                    }
                }
            }
        }
        var faceEmissionMs = Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds;

        stageStarted = Stopwatch.GetTimestamp();
        var completedPages = pages
            .OrderBy(static pair => pair.Key.X)
            .ThenBy(static pair => pair.Key.Y)
            .ThenBy(static pair => pair.Key.Z)
            .Select(pair => pair.Value.Build(pair.Key, guard))
            .ToArray();
        var flatteningMs = Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds;
        var constructionPages = completedPages.Length;
        var coalescingMs = 0.0;
        if (tile.Key.Level >= TileScaleSubmissionMinimumLevel)
        {
            stageStarted = Stopwatch.GetTimestamp();
            completedPages = CoalescePages(completedPages, guard);
            coalescingMs = Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds;
        }
        return new TerrainLodSpatialMeshData(
            tile.Key,
            tile.CanonicalHash,
            tile.HorizontalSampleLevel,
            verticalSliceBudget,
            emitTileBoundaryFaces,
            maximumRenderedSpans,
            completedPages,
            new TerrainLodSpatialMeshBuildProfile(
                reductionMs,
                faceEmissionMs,
                flatteningMs,
                coalescingMs,
                columns.Length,
                sourceSpans,
                constructionPages, canonicalSpans, caveCulledColumns,
                verticalReducedColumns, renderedSpans)) { CaveCullBelowY = caveCullBelowY };

        TerrainLodColumn Column(int x, int z) => columns[x * tile.Width + z];
        bool InBounds(int x, int z) =>
            (uint)x < (uint)tile.Width && (uint)z < (uint)tile.Width;

        PageBuilder PageFor(double worldX, double worldY, double worldZ)
        {
            var key = new TerrainLodSpatialMeshPageKey(
                FloorDivide((int)Math.Floor(worldX), PageSize),
                FloorDivide((int)Math.Floor(worldY), PageSize),
                FloorDivide((int)Math.Floor(worldZ), PageSize));
            if (!pages.TryGetValue(key, out var page))
            {
                page = new PageBuilder(
                    checked(key.X * PageSize),
                    checked(key.Y * PageSize),
                    checked(key.Z * PageSize));
                pages.Add(key, page);
            }
            return page;
        }

        TerrainLodFaceAppearance Appearance(
            Block owner,
            TerrainLodMaterial material,
            Side side,
            TerrainLodColumnSpan? above,
            int sampleX,
            int sampleZ)
        {
            var tint = tile.Climate is { } climate
                ? ClimateTint(owner, grassBlock, leavesBlock, tallGrassBlock, material,
                    climate[sampleX, sampleZ])
                : null;
            return TerrainLodMeshBuilder.ResolveWorldlessFaceAppearance(
                owner, material, side, ReferenceEquals(owner, grassBlock),
                TerrainLodMeshBuilder.HasSnowCover(above),
                grassOverlayTexture, snowyGrassTexture, tint);
        }

        TerrainLodQuadLighting SmoothLighting(
            Side side,
            TerrainLodMaterial material,
            ChunkLightVertex flat,
            (float X, float Y, float Z) a,
            (float X, float Y, float Z) b,
            (float X, float Y, float Z) c,
            (float X, float Y, float Z) d)
        {
            // Near LOD retains the original 1x1 columns. Average the four exposed cells
            // meeting each corner, following the exact cube renderer's smooth-light layout. Coarse
            // levels, liquids and inset shapes keep flat light: their vertices do not map to
            // individual block corners. Tile-edge corners lack a neighbor tile, so retain the
            // face's light instead of inventing a dark seam.
            if (sampleSize != 1 || material.Geometry is not
                    (TerrainLodGeometryClass.Opaque or TerrainLodGeometryClass.Cutout))
                return TerrainLodQuadLighting.Uniform(flat);
            return new TerrainLodQuadLighting(
                Corner(side, a, flat), Corner(side, b, flat),
                Corner(side, c, flat), Corner(side, d, flat));
        }

        ChunkLightVertex Corner(
            Side side,
            (float X, float Y, float Z) vertex,
            ChunkLightVertex fallback)
        {
            var worldX = (int)MathF.Round(vertex.X);
            var worldY = (int)MathF.Round(vertex.Y);
            var worldZ = (int)MathF.Round(vertex.Z);
            if (MathF.Abs(vertex.X - worldX) > 0.001f ||
                MathF.Abs(vertex.Y - worldY) > 0.001f ||
                MathF.Abs(vertex.Z - worldZ) > 0.001f)
                return fallback;

            var firstX = side == Side.East ? worldX : worldX - 1;
            var firstZ = side == Side.South ? worldZ : worldZ - 1;
            var firstY = side == Side.Up ? worldY : worldY - 1;
            var xCount = side is Side.West or Side.East ? 1 : 2;
            var yCount = side is Side.Up or Side.Down ? 1 : 2;
            var zCount = side is Side.North or Side.South ? 1 : 2;
            var sky = 0;
            var block = 0;
            for (var dx = 0; dx < xCount; dx++)
            for (var dy = 0; dy < yCount; dy++)
            for (var dz = 0; dz < zCount; dz++)
            {
                var sampleX = firstX + dx - (int)tileMinX;
                var sampleZ = firstZ + dz - (int)tileMinZ;
                var sampleY = firstY + dy;
                if (!InBounds(sampleX, sampleZ) || sampleY < 0 || sampleY >= tile.WorldHeight)
                    return fallback;
                var sample = Column(sampleX, sampleZ).At(sampleY);
                sky += sample.SkyLight;
                block += sample.BlockLight;
            }
            var count = xCount * yCount * zCount;
            return new ChunkLightVertex(
                ChunkVertexHelper.ToQuarterLevels((float)sky / count),
                ChunkVertexHelper.ToQuarterLevels((float)block / count));
        }
    }

    internal static int? ClimateTint(
        Block owner,
        Block? grassBlock,
        Block? leavesBlock,
        Block? tallGrassBlock,
        TerrainLodMaterial material,
        TerrainLodClimateSample sample)
    {
        // Only the shipped biome-tinted visuals are mapped here. Other blocks retain their
        // definition tint until LOD visual providers become part of the content runtime.
        if (ReferenceEquals(owner, grassBlock))
            return GrassColors.getColor(sample.TemperatureValue, sample.DownfallValue);
        if (ReferenceEquals(owner, leavesBlock))
            return (material.Metadata & 1) != 0
                ? FoliageColors.getSpruceColor()
                : (material.Metadata & 2) != 0
                    ? FoliageColors.getBirchColor()
                    : FoliageColors.getFoliageColor(
                        sample.TemperatureValue, sample.DownfallValue);
        if (ReferenceEquals(owner, tallGrassBlock))
            return material.Metadata == 0 ? 0xFFFFFF : GrassColors.getColor(
                sample.TemperatureValue, sample.DownfallValue);
        return null;
    }

    private static TerrainLodColumnSpan? NeighborAt(TerrainLodColumn column, int y) =>
        y < 0 || y >= column.WorldHeight ? null : column.At(y);

    internal static bool IsFaceVisible(
        TerrainLodMaterial material,
        TerrainLodColumnSpan? neighbor,
        bool translucent,
        IBlockRuntimeView blocks)
    {
        if (neighbor is null || neighbor.Value.IsAir) return true;
        if (neighbor.Value.Material.OccludesFaces) return false;
        if (material.Geometry == TerrainLodGeometryClass.SurfaceLayer &&
            neighbor.Value.Material == material) return false;
        if (translucent && TerrainLodMeshBuilder.SharesLiquidMedium(
                material, neighbor.Value.Material, blocks))
            return false;
        return !translucent || neighbor.Value.Material != material;
    }

    internal static bool TryLayer(TerrainLodMaterial material, int sampleSize, out bool translucent)
    {
        translucent = TerrainLodMeshBuilder.IsTranslucent(material);
        return translucent || TerrainLodMeshBuilder.IsVolumetricDepthWriting(material) ||
               (sampleSize == 1 && material.Geometry == TerrainLodGeometryClass.SurfaceLayer);
    }

    internal static bool IsHorizontallyInsetOnSide(Block block, Side side)
    {
        var bounds = block.DefinitionBoundingBox;
        const double epsilon = 0.0001;
        return side switch
        {
            Side.West => bounds.MinX > epsilon,
            Side.East => bounds.MaxX < 1 - epsilon,
            Side.North => bounds.MinZ > epsilon,
            Side.South => bounds.MaxZ < 1 - epsilon,
            _ => false
        };
    }

    internal static ChunkLightVertex Light(in TerrainLodColumnSpan span) => new(
        ChunkVertexHelper.ToQuarterLevels(span.SkyLight),
        ChunkVertexHelper.ToQuarterLevels(span.BlockLight));

    /// <summary>
    ///     Samples the medium exposed by a face, not the opaque span that owns its material.
    ///     Opaque blocks normally store zero skylight internally; using that value for their top
    ///     face turns otherwise sunlit distant terrain black. Source block light remains a floor
    ///     so luminous materials do not darken when the adjacent air sample is empty.
    /// </summary>
    internal static ChunkLightVertex FaceLight(
        in TerrainLodColumnSpan source,
        TerrainLodColumnSpan? exposedNeighbor,
        Side side)
    {
        // A missing horizontal neighbor is an exterior presentation frontier, not an opaque
        // underground sample. Light its bounded skirt as open air; otherwise the frontier becomes
        // a solid black rectangle even at noon. Downward world-bottom faces retain source light.
        var sky = exposedNeighbor?.SkyLight ?? side switch
        {
            Side.Down => source.SkyLight,
            _ => (byte)15
        };
        var block = Math.Max(source.BlockLight, exposedNeighbor?.BlockLight ?? (byte)0);
        return new ChunkLightVertex(
            ChunkVertexHelper.ToQuarterLevels(sky),
            ChunkVertexHelper.ToQuarterLevels(block));
    }

    internal static void Emit(
        PageBuilder page,
        bool translucent,
        Side side,
        TerrainLodFaceAppearance appearance,
        float shade,
        ChunkLightVertex light,
        float tileU,
        float tileV,
        (float X, float Y, float Z) a,
        (float X, float Y, float Z) b,
        (float X, float Y, float Z) c,
        (float X, float Y, float Z) d,
        TerrainLodSpatialMeshBuildGuard? guard = null,
        bool nonDirectional = false)
        => Emit(page, translucent, side, appearance, shade,
            TerrainLodQuadLighting.Uniform(light), tileU, tileV,
            a, b, c, d, guard, nonDirectional);

    internal static void Emit(
        PageBuilder page,
        bool translucent,
        Side side,
        TerrainLodFaceAppearance appearance,
        float shade,
        TerrainLodQuadLighting lights,
        float tileU,
        float tileV,
        (float X, float Y, float Z) a,
        (float X, float Y, float Z) b,
        (float X, float Y, float Z) c,
        (float X, float Y, float Z) d,
        TerrainLodSpatialMeshBuildGuard? guard = null,
        bool nonDirectional = false)
    {
        EmitTexture(appearance.Texture, appearance.Tint);
        if (appearance.OverlayTexture >= 0)
            EmitTexture(appearance.OverlayTexture, appearance.OverlayTint);
        return;

        void EmitTexture(int texture, int tint)
        {
            guard?.ReserveQuad();
            var layer = Atlases.Terrain.LayerOfGridIndex(texture);
            var color = TerrainLodMeshBuilder.PackTintedColor(tint, shade);
            var uvScaleExponent = UvScaleExponent(Math.Max(tileU, tileV));
            var uvScale = 1 << uvScaleExponent;
            if (nonDirectional)
                // Crossed plants use the same UV orientation as local level-zero plants.
                page.AddUnassigned(translucent, lights,
                    Vertex(a, 0, 0), Vertex(b, 0, tileV),
                    Vertex(c, tileU, tileV), Vertex(d, tileU, 0));
            else
                page.Add(translucent, side, lights,
                    Vertex(a, tileU, 0), Vertex(b, tileU, tileV),
                    Vertex(c, 0, tileV), Vertex(d, 0, 0));

            ChunkVertex Vertex((float X, float Y, float Z) value, float u, float v) =>
                ChunkVertexHelper.Create(
                    color,
                    value.X - page.OriginX,
                    value.Y - page.OriginY,
                    value.Z - page.OriginZ,
                    u / uvScale, v / uvScale, layer, uvScaleExponent);
        }
    }

    internal static byte UvScaleExponent(float maximumUv)
    {
        if (!float.IsFinite(maximumUv) || maximumUv < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumUv));
        byte exponent = 0;
        while (maximumUv > MaximumQuadSpan)
        {
            maximumUv *= 0.5f;
            exponent++;
        }
        return exponent;
    }

    internal static int FloorDivide(int value, int divisor)
    {
        var quotient = value / divisor;
        return value % divisor < 0 ? quotient - 1 : quotient;
    }

    /// <summary>
    ///     Converts the fixed-size CPU construction pages of one distant tile into one GPU
    ///     submission. Local packed positions stay inside their original 64-block pages; the
    ///     otherwise-unused vertex lanes carry that page's offset from the new tile origin.
    /// </summary>
    internal static TerrainLodSpatialMeshPage[] CoalescePages(
        IReadOnlyList<TerrainLodSpatialMeshPage> pages,
        TerrainLodSpatialMeshBuildGuard? guard = null)
    {
        if (pages.Count <= 1) return pages.ToArray();

        var originX = pages.Min(static page => page.OriginX);
        var originY = pages.Min(static page => page.OriginY);
        var originZ = pages.Min(static page => page.OriginZ);
        var maximumX = pages.Max(static page => checked(page.OriginX + page.ExtentX));
        var maximumY = pages.Max(static page => checked(page.OriginY + page.ExtentY));
        var maximumZ = pages.Max(static page => checked(page.OriginZ + page.ExtentZ));

        var solid = MergeLayer(pages, translucent: false, originX, originY, originZ, guard);
        var translucent = MergeLayer(pages, translucent: true, originX, originY, originZ, guard);
        return
        [
            new TerrainLodSpatialMeshPage(
                new TerrainLodSpatialMeshPageKey(
                    FloorDivide(originX, PageSize),
                    FloorDivide(originY, PageSize),
                    FloorDivide(originZ, PageSize)),
                originX, originY, originZ,
                checked(maximumX - originX),
                checked(maximumY - originY),
                checked(maximumZ - originZ),
                solid.Vertices, solid.Lights, solid.Ranges,
                translucent.Vertices, translucent.Lights, translucent.Ranges)
        ];
    }

    private static CoalescedLayer MergeLayer(
        IReadOnlyList<TerrainLodSpatialMeshPage> pages,
        bool translucent,
        int originX,
        int originY,
        int originZ,
        TerrainLodSpatialMeshBuildGuard? guard)
    {
        List<ChunkVertex> vertices = [];
        List<ChunkLightVertex> lights = [];
        Span<ChunkQuadRange> mergedRanges = stackalloc ChunkQuadRange[7];
        for (var bucket = 0; bucket < 7; bucket++)
        {
            guard?.Checkpoint();
            var firstQuad = vertices.Count / 4;
            foreach (var page in pages)
            {
                var pageVertices = translucent ? page.TranslucentVertices : page.Vertices;
                var pageLights = translucent ? page.TranslucentLights : page.Lights;
                var ranges = translucent ? page.TranslucentRanges : page.SolidRanges;
                var range = bucket < 6 ? ranges.Get((Side)bucket) : ranges.Unassigned;
                if (range.IsEmpty) continue;

                var offsetX = CheckedPageOffset(page.OriginX, originX);
                var offsetY = CheckedPageOffset(page.OriginY, originY);
                var offsetZ = CheckedPageOffset(page.OriginZ, originZ);
                var packedXZ = (short)(ushort)(offsetX | offsetZ << 8);
                var firstVertex = checked(range.FirstQuad * 4);
                var vertexCount = checked(range.QuadCount * 4);
                for (var index = 0; index < vertexCount; index++)
                {
                    var vertex = pageVertices[firstVertex + index];
                    vertex.PageOffsetXZ = packedXZ;
                    vertex.PageOffsetY = (byte)offsetY;
                    vertices.Add(vertex);
                    lights.Add(pageLights[firstVertex + index]);
                }
            }
            mergedRanges[bucket] = new ChunkQuadRange(
                firstQuad, vertices.Count / 4 - firstQuad);
        }

        return new CoalescedLayer(
            [.. vertices], [.. lights],
            new ChunkDirectionalRanges(
                mergedRanges[0], mergedRanges[1], mergedRanges[2], mergedRanges[3],
                mergedRanges[4], mergedRanges[5], mergedRanges[6]));

        static int CheckedPageOffset(int pageOrigin, int rootOrigin)
        {
            var delta = checked(pageOrigin - rootOrigin);
            if (delta < 0 || delta % PageSize != 0 || delta / PageSize > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(pageOrigin),
                    $"Distant terrain page delta {delta} cannot be encoded in one byte of {PageSize}-block units.");
            return delta / PageSize;
        }
    }

    private readonly record struct CoalescedLayer(
        ChunkVertex[] Vertices,
        ChunkLightVertex[] Lights,
        ChunkDirectionalRanges Ranges);

    internal sealed class PageBuilder(int originX, int originY, int originZ)
    {
        private readonly List<Quad>[] _solid = CreateBuckets();
        private readonly List<Quad>[] _translucent = CreateBuckets();

        public int OriginX { get; } = originX;
        public int OriginY { get; } = originY;
        public int OriginZ { get; } = originZ;

        public void Add(
            bool translucent,
            Side side,
            ChunkLightVertex light,
            ChunkVertex a,
            ChunkVertex b,
            ChunkVertex c,
            ChunkVertex d) => Add(translucent, side, TerrainLodQuadLighting.Uniform(light), a, b, c, d);

        public void Add(
            bool translucent,
            Side side,
            TerrainLodQuadLighting lights,
            ChunkVertex a,
            ChunkVertex b,
            ChunkVertex c,
            ChunkVertex d)
        {
            var bucket = (int)side;
            if ((uint)bucket >= 6) throw new ArgumentOutOfRangeException(nameof(side));
            (translucent ? _translucent : _solid)[bucket].Add(new Quad(a, b, c, d, lights));
        }

        public void AddUnassigned(
            bool translucent,
            TerrainLodQuadLighting lights,
            ChunkVertex a,
            ChunkVertex b,
            ChunkVertex c,
            ChunkVertex d) =>
            (translucent ? _translucent : _solid)[6].Add(new Quad(a, b, c, d, lights));

        public TerrainLodSpatialMeshPage Build(
            TerrainLodSpatialMeshPageKey key,
            TerrainLodSpatialMeshBuildGuard? guard = null)
        {
            guard?.Checkpoint();
            var solid = Flatten(_solid, mergeHorizontal: false, guard);
            var translucent = Flatten(_translucent, mergeHorizontal: true, guard);
            return new TerrainLodSpatialMeshPage(
                key, OriginX, OriginY, OriginZ,
                PageSize, PageSize, PageSize,
                solid.Vertices, solid.Lights, solid.Ranges,
                translucent.Vertices, translucent.Lights, translucent.Ranges);
        }

        private static List<Quad>[] CreateBuckets() =>
            Enumerable.Range(0, 7).Select(static _ => new List<Quad>()).ToArray();

        private static LayerData Flatten(
            List<Quad>[] buckets,
            bool mergeHorizontal,
            TerrainLodSpatialMeshBuildGuard? guard)
        {
            List<ChunkVertex> vertices = [];
            List<ChunkLightVertex> lights = [];
            Span<ChunkQuadRange> ranges = stackalloc ChunkQuadRange[7];
            for (var bucket = 0; bucket < buckets.Length; bucket++)
            {
                guard?.Checkpoint();
                var firstQuad = vertices.Count / 4;
                List<ChunkVertex> bucketVertices = [];
                List<ChunkLightVertex> bucketLights = [];
                foreach (var quad in buckets[bucket])
                {
                    bucketVertices.Add(quad.A);
                    bucketVertices.Add(quad.B);
                    bucketVertices.Add(quad.C);
                    bucketVertices.Add(quad.D);
                    bucketLights.Add(quad.Lights.A);
                    bucketLights.Add(quad.Lights.B);
                    bucketLights.Add(quad.Lights.C);
                    bucketLights.Add(quad.Lights.D);
                }
                var merged = mergeHorizontal &&
                             bucket is (int)Side.Down or (int)Side.Up
                    ? TerrainLodHorizontalQuadMerger.Merge(
                        [.. bucketVertices],
                        [.. bucketLights])
                    : new TerrainLodHorizontalQuadMerger.MergedQuads(
                        [.. bucketVertices], [.. bucketLights]);
                vertices.AddRange(merged.Vertices);
                lights.AddRange(merged.Lights);
                ranges[bucket] = new ChunkQuadRange(firstQuad, merged.Vertices.Length / 4);
            }
            return new LayerData(
                [.. vertices], [.. lights],
                new ChunkDirectionalRanges(
                    ranges[0], ranges[1], ranges[2], ranges[3],
                    ranges[4], ranges[5], ranges[6]));
        }

        private readonly record struct Quad(
            ChunkVertex A,
            ChunkVertex B,
            ChunkVertex C,
            ChunkVertex D,
            TerrainLodQuadLighting Lights);

        private readonly record struct LayerData(
            ChunkVertex[] Vertices,
            ChunkLightVertex[] Lights,
            ChunkDirectionalRanges Ranges);
    }
}
