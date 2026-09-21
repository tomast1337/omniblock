using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Registries;
using OmniBlock.Textures;
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

/// <summary>Immutable CPU result for one canonical spatial column tile.</summary>
internal sealed record TerrainLodSpatialMeshData(
    TerrainLodTileKey Key,
    string CanonicalHash,
    int HorizontalSampleLevel,
    int VerticalSliceBudget,
    bool IncludesExternalBoundaryFaces,
    int MaximumRenderedSpans,
    TerrainLodSpatialMeshPage[] Pages)
{
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

        var columns = new TerrainLodColumn[checked(tile.Width * tile.Width)];
        var maximumRenderedSpans = 0;
        for (var x = 0; x < tile.Width; x++)
        for (var z = 0; z < tile.Width; z++)
        {
            guard.Checkpoint();
            var source = caveCullBelowY is { } ceilingY
                ? TerrainLodCaveCuller.SealUndergroundAir(tile[x, z], ceilingY)
                : tile[x, z];
            var reduced = TerrainLodVerticalSliceReducer.Reduce(source, verticalSliceBudget);
            columns[x * tile.Width + z] = reduced;
            maximumRenderedSpans = Math.Max(maximumRenderedSpans, reduced.Spans.Count);
        }

        Dictionary<TerrainLodSpatialMeshPageKey, PageBuilder> pages = [];
        blocks.TryGet("omniblock:grass_block", out var grassBlock);
        var grassOverlayTexture = grassBlock is null
            ? -1
            : Atlases.Terrain.IndexOf("omniblock:grass_block_side_overlay");

        for (var x = 0; x < tile.Width; x++)
        for (var z = 0; z < tile.Width; z++)
        {
            guard.Checkpoint();
            var column = Column(x, z);
            foreach (var span in column.Spans)
            {
                if (!TryLayer(span.Material, out var translucent)) continue;
                if (!blocks.TryGet(span.Material.BlockId, out var block) || block is null) continue;

                var minX = checked((int)tileMinX + x * sampleSize);
                var maxX = checked(minX + sampleSize);
                var minZ = checked((int)tileMinZ + z * sampleSize);
                var maxZ = checked(minZ + sampleSize);
                var minY = span.BottomY;
                var maxY = span.TopY;
                var renderMaxY = (float)maxY;
                if (span.Material.Geometry == TerrainLodGeometryClass.Liquid &&
                    IsFaceVisible(span.Material, NeighborAt(column, maxY), translucent, blocks))
                    renderMaxY -= FluidMath.GetFluidHeightFromMeta(span.Material.Metadata);

                var below = NeighborAt(column, minY - 1);
                var above = NeighborAt(column, maxY);
                if (IsFaceVisible(span.Material, below, translucent, blocks))
                    EmitHorizontal(Side.Down, minY, anchorY: minY, 0.5f, below);
                if (IsFaceVisible(span.Material, above, translucent, blocks))
                    EmitHorizontal(Side.Up, renderMaxY, anchorY: maxY - 1, 1.0f, above);

                EmitSide(Side.West, x - 1, z, minX, minZ, maxZ, 0.6f);
                EmitSide(Side.East, x + 1, z, maxX, minZ, maxZ, 0.6f);
                EmitSide(Side.North, x, z - 1, minZ, minX, maxX, 0.8f);
                EmitSide(Side.South, x, z + 1, maxZ, minX, maxX, 0.8f);

                void EmitHorizontal(
                    Side side,
                    float faceY,
                    int anchorY,
                    float shade,
                    TerrainLodColumnSpan? exposedNeighbor)
                {
                    var appearance = Appearance(block, span.Material, side);
                    var light = FaceLight(span, exposedNeighbor, side);
                    // sampleSize is bounded by MaximumSampleSpan and the power-of-two sample grid is
                    // aligned to every page boundary, so this quad can never straddle a page.
                    var page = PageFor(
                        minX + sampleSize / 2.0,
                        Math.Clamp(anchorY, 0, tile.WorldHeight - 1) + 0.5,
                        minZ + sampleSize / 2.0);
                    if (side == Side.Up)
                        Emit(page, translucent, side, appearance, shade, light, sampleSize, sampleSize,
                            (maxX, faceY, maxZ), (maxX, faceY, minZ),
                            (minX, faceY, minZ), (minX, faceY, maxZ), guard);
                    else
                        Emit(page, translucent, side, appearance, shade, light, sampleSize, sampleSize,
                            (minX, faceY, maxZ), (minX, faceY, minZ),
                            (maxX, faceY, minZ), (maxX, faceY, maxZ), guard);
                }

                void EmitSide(
                    Side side,
                    int neighborX,
                    int neighborZ,
                    int fixedCoordinate,
                    int alongStart,
                    int alongEnd,
                    float shade)
                {
                    var neighborInBounds = InBounds(neighborX, neighborZ);
                    if (!neighborInBounds && !emitTileBoundaryFaces) return;
                    var neighbor = neighborInBounds ? Column(neighborX, neighborZ) : null;
                    var runStart = -1;
                    var runLight = default(ChunkLightVertex);
                    for (var y = minY; y <= maxY; y++)
                    {
                        if ((y & 15) == 0) guard.Checkpoint();
                        var adjacent = y < maxY && neighbor is not null
                            ? NeighborAt(neighbor, y)
                            : null;
                        var visible = y < maxY && IsFaceVisible(
                            span.Material, adjacent, translucent, blocks);
                        var light = FaceLight(span, adjacent, side);
                        if (visible && runStart >= 0 && light != runLight)
                        {
                            EmitVerticalRange(runStart, y, runLight);
                            runStart = y;
                            runLight = light;
                        }
                        else if (visible && runStart < 0)
                        {
                            runStart = y;
                            runLight = light;
                        }
                        if (visible || runStart < 0) continue;

                        var runEnd = y == maxY ? renderMaxY : y;
                        EmitVerticalRange(runStart, runEnd, runLight);
                        runStart = -1;
                    }

                    void EmitVerticalRange(
                        float bottom,
                        float top,
                        ChunkLightVertex light)
                    {
                        for (var y0 = bottom; y0 < top;)
                        {
                            var pageBoundary = (FloorDivide((int)MathF.Floor(y0), PageSize) + 1) * PageSize;
                            var y1 = Math.Min(top, Math.Min(y0 + MaximumQuadSpan, pageBoundary));
                            if (y1 <= y0) y1 = Math.Min(top, y0 + MaximumQuadSpan);
                            var appearance = Appearance(block, span.Material, side);
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
                                    Emit(page, translucent, side, appearance, shade, light,
                                        alongEnd - alongStart, height,
                                        (fixedCoordinate, y1, alongStart),
                                        (fixedCoordinate, y0, alongStart),
                                        (fixedCoordinate, y0, alongEnd),
                                        (fixedCoordinate, y1, alongEnd), guard);
                                    break;
                                case Side.East:
                                    Emit(page, translucent, side, appearance, shade, light,
                                        alongEnd - alongStart, height,
                                        (fixedCoordinate, y1, alongEnd),
                                        (fixedCoordinate, y0, alongEnd),
                                        (fixedCoordinate, y0, alongStart),
                                        (fixedCoordinate, y1, alongStart), guard);
                                    break;
                                case Side.North:
                                    Emit(page, translucent, side, appearance, shade, light,
                                        alongEnd - alongStart, height,
                                        (alongEnd, y1, fixedCoordinate),
                                        (alongEnd, y0, fixedCoordinate),
                                        (alongStart, y0, fixedCoordinate),
                                        (alongStart, y1, fixedCoordinate), guard);
                                    break;
                                case Side.South:
                                    Emit(page, translucent, side, appearance, shade, light,
                                        alongEnd - alongStart, height,
                                        (alongStart, y1, fixedCoordinate),
                                        (alongStart, y0, fixedCoordinate),
                                        (alongEnd, y0, fixedCoordinate),
                                        (alongEnd, y1, fixedCoordinate), guard);
                                    break;
                            }
                            y0 = y1;
                        }
                    }
                }
            }
        }

        var completedPages = pages
            .OrderBy(static pair => pair.Key.X)
            .ThenBy(static pair => pair.Key.Y)
            .ThenBy(static pair => pair.Key.Z)
            .Select(pair => pair.Value.Build(pair.Key, guard))
            .ToArray();
        if (tile.Key.Level >= TileScaleSubmissionMinimumLevel)
            completedPages = CoalescePages(completedPages, guard);
        return new TerrainLodSpatialMeshData(
            tile.Key,
            tile.CanonicalHash,
            tile.HorizontalSampleLevel,
            verticalSliceBudget,
            emitTileBoundaryFaces,
            maximumRenderedSpans,
            completedPages);

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
            Side side)
        {
            var tint = owner.GetColorForFace(material.Metadata, (int)side);
            return TerrainLodMeshBuilder.ResolveFaceAppearance(
                owner, material.Metadata, side, tint,
                ReferenceEquals(owner, grassBlock), grassOverlayTexture);
        }
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
        if (translucent && TerrainLodMeshBuilder.SharesLiquidMedium(
                material, neighbor.Value.Material, blocks))
            return false;
        return !translucent || neighbor.Value.Material != material;
    }

    internal static bool TryLayer(TerrainLodMaterial material, out bool translucent)
    {
        translucent = TerrainLodMeshBuilder.IsTranslucent(material);
        return translucent || TerrainLodMeshBuilder.IsVolumetricDepthWriting(material);
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
        TerrainLodSpatialMeshBuildGuard? guard = null)
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
            page.Add(translucent, side, light,
                Vertex(a, tileU, 0),
                Vertex(b, tileU, tileV),
                Vertex(c, 0, tileV),
                Vertex(d, 0, 0));

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
            ChunkVertex d)
        {
            var bucket = (int)side;
            if ((uint)bucket >= 6) throw new ArgumentOutOfRangeException(nameof(side));
            (translucent ? _translucent : _solid)[bucket].Add(new Quad(a, b, c, d, light));
        }

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
            Enumerable.Range(0, 6).Select(static _ => new List<Quad>()).ToArray();

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
                    for (var index = 0; index < 4; index++) bucketLights.Add(quad.Light);
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
            ranges[6] = new ChunkQuadRange(vertices.Count / 4, 0);
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
            ChunkLightVertex Light);

        private readonly record struct LayerData(
            ChunkVertex[] Vertices,
            ChunkLightVertex[] Lights,
            ChunkDirectionalRanges Ranges);
    }
}
