using System.Security.Cryptography;
using System.Text;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Registries;
using OmniBlock.Textures;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

/// <summary>An immutable CPU seam compiled from the two selected spatial presentations.</summary>
internal sealed record TerrainLodSpatialSeamMeshData(
    TerrainLodSpatialSeamSegment Segment,
    string CanonicalHash,
    TerrainLodSpatialMeshPage[] Pages)
{
    public int SolidQuadCount => Pages.Sum(static page => page.Vertices.Length / 4);
    public int TranslucentQuadCount => Pages.Sum(static page =>
        page.TranslucentVertices.Length / 4);
    public long EstimatedBytes => Pages.Sum(static page => page.EstimatedBytes);
    public int[] ArenaAllocationVertexCounts => Pages
        .SelectMany(static page => new[]
        {
            page.Vertices.Length,
            page.TranslucentVertices.Length
        })
        .Where(static count => count > 0)
        .ToArray();
}

/// <summary>
///     Builds the horizontal boundary between the exact tile selections that form one spatial
///     presentation frame. The finer of the two sample grids partitions the edge; both reduced
///     vertical columns then participate in visibility, material, liquid-height, and lighting
///     decisions. This is deliberately separate from tile-body construction so changing an
///     adjacent LOD never requires rebuilding either tile body.
/// </summary>
internal static class TerrainLodSpatialSeamMeshBuilder
{
    // Unknown terrain must never expose a full bedrock-to-surface cross-section. A shallow skirt
    // hides cracks at the finite coverage edge while contiguous coarse coverage is acquired.
    internal const int ExteriorSkirtDepth = 16;

    public static TerrainLodSpatialSeamMeshData Build(
        TerrainLodSpatialSeamSegment segment,
        TerrainLodColumnTile owner,
        TerrainLodColumnTile? neighbor,
        IBlockRuntimeView blocks,
        int? caveCullBelowY = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(blocks);
        if (owner.Key != segment.Owner.Tile)
            throw new ArgumentException(
                $"Seam owner data is {owner.Key}; expected {segment.Owner.Tile}.", nameof(owner));
        if (segment.Neighbor is { } neighborSelection)
        {
            ArgumentNullException.ThrowIfNull(neighbor);
            if (neighbor.Key != neighborSelection.Tile)
                throw new ArgumentException(
                    $"Seam neighbor data is {neighbor.Key}; expected {neighborSelection.Tile}.",
                    nameof(neighbor));
            if (neighbor.WorldHeight != owner.WorldHeight)
                throw new ArgumentException("Spatial seam tiles must share a world height.",
                    nameof(neighbor));
        }
        else if (neighbor is not null)
            throw new ArgumentException("An exterior spatial seam cannot have neighbor data.",
                nameof(neighbor));
        if (segment.AlongStartChunk >= segment.AlongEndChunk)
            throw new ArgumentException("A spatial seam must cover a non-empty interval.",
                nameof(segment));

        var ownerSample = checked(1 << owner.HorizontalSampleLevel);
        var neighborSample = neighbor is null
            ? ownerSample
            : checked(1 << neighbor.HorizontalSampleLevel);
        var step = Math.Min(ownerSample, neighborSample);
        if (step > TerrainLodSpatialMeshBuilder.MaximumSampleSpan)
            throw new NotSupportedException(
                $"Spatial seam sample size {step} exceeds the packed position-page limit.");

        var fixedBlock = CheckedBlock(segment.FixedChunkCoordinate);
        var alongStart = CheckedBlock(segment.AlongStartChunk);
        var alongEnd = CheckedBlock(segment.AlongEndChunk);
        Dictionary<TerrainLodSpatialMeshPageKey, TerrainLodSpatialMeshBuilder.PageBuilder> pages = [];
        Dictionary<(TerrainLodColumnTile Tile, int X, int Z, int Budget), TerrainLodColumn> reduced = [];
        blocks.TryGet("omniblock:grass_block", out var grassBlock);
        var grassOverlayTexture = grassBlock is null
            ? -1
            : Atlases.Terrain.IndexOf("omniblock:grass_block_side_overlay");

        for (var along = alongStart; along < alongEnd; along += step)
        {
            var end = Math.Min(alongEnd, checked(along + step));
            var ownerColumn = ColumnAt(
                owner, segment.Owner.MaximumVerticalSlices,
                SourceX(segment.OwnerSide, fixedBlock, along, ownerSide: true),
                SourceZ(segment.OwnerSide, fixedBlock, along, ownerSide: true));
            var neighborColumn = neighbor is null
                ? null
                : ColumnAt(
                    neighbor, segment.Neighbor!.Value.MaximumVerticalSlices,
                    SourceX(segment.OwnerSide, fixedBlock, along, ownerSide: false),
                    SourceZ(segment.OwnerSide, fixedBlock, along, ownerSide: false));
            var exteriorBottom = neighborColumn is null
                ? Math.Max(0, SurfaceTop(ownerColumn) - ExteriorSkirtDepth)
                : 0;

            SortedSet<int> transitions = [0, owner.WorldHeight];
            AddTransitions(transitions, ownerColumn);
            if (neighborColumn is not null) AddTransitions(transitions, neighborColumn);
            var heights = transitions.ToArray();
            for (var index = 0; index < heights.Length - 1; index++)
            {
                var bottom = heights[index];
                var top = heights[index + 1];
                var ownerSpan = ownerColumn.At(bottom);
                TerrainLodColumnSpan? neighborSpan = neighborColumn?.At(bottom);
                if (neighborSpan is { } liquidNeighbor &&
                    TerrainLodMeshBuilder.SharesLiquidMedium(
                        ownerSpan.Material, liquidNeighbor.Material, blocks))
                {
                    EmitLiquidReconciliation(ownerSpan, ownerColumn,
                        liquidNeighbor, neighborColumn!, bottom, top);
                    continue;
                }
                EmitVisible(
                    ownerSpan, ownerColumn, neighborSpan, segment.OwnerSide,
                    neighborColumn is null ? exteriorBottom : bottom);
                if (neighborSpan is { } adjacent)
                    EmitVisible(
                        adjacent, neighborColumn!, ownerSpan, Opposite(segment.OwnerSide), bottom);

                void EmitVisible(
                    TerrainLodColumnSpan source,
                    TerrainLodColumn sourceColumn,
                    TerrainLodColumnSpan? opposite,
                    TerrainLodSpatialBoundarySide sourceSide,
                    int minimumY)
                {
                    if (!TerrainLodSpatialMeshBuilder.TryLayer(
                        source.Material, out var translucent) ||
                        !TerrainLodSpatialMeshBuilder.IsFaceVisible(
                            source.Material, opposite, translucent, blocks) ||
                        !blocks.TryGet(source.Material.BlockId, out var block) || block is null)
                        return;

                    var renderBottom = Math.Max(bottom, minimumY);
                    var renderTop = (float)top;
                    if (source.Material.Geometry == TerrainLodGeometryClass.Liquid &&
                        top == source.TopY && top < sourceColumn.WorldHeight &&
                        sourceColumn.At(top).IsAir)
                        renderTop -= FluidMath.GetFluidHeightFromMeta(source.Material.Metadata);
                    if (renderTop <= renderBottom) return;

                    for (var y0 = (float)renderBottom; y0 < renderTop;)
                    {
                        var pageBoundary =
                            (TerrainLodSpatialMeshBuilder.FloorDivide(
                                 (int)MathF.Floor(y0), TerrainLodSpatialMeshBuilder.PageSize) + 1) *
                            TerrainLodSpatialMeshBuilder.PageSize;
                        var y1 = Math.Min(renderTop,
                            Math.Min(y0 + TerrainLodSpatialMeshBuilder.MaximumQuadSpan, pageBoundary));
                        if (y1 <= y0) y1 = Math.Min(renderTop,
                            y0 + TerrainLodSpatialMeshBuilder.MaximumQuadSpan);
                        EmitPatch(
                            source, opposite, block, translucent, sourceSide,
                            y0, y1, along, end);
                        y0 = y1;
                    }
                }

                void EmitLiquidReconciliation(
                    TerrainLodColumnSpan ownerLiquid,
                    TerrainLodColumn ownerLiquidColumn,
                    TerrainLodColumnSpan neighborLiquid,
                    TerrainLodColumn neighborLiquidColumn,
                    int intervalBottom,
                    int intervalTop)
                {
                    var ownerTop = LiquidTop(ownerLiquid, ownerLiquidColumn, intervalTop);
                    var neighborTop = LiquidTop(
                        neighborLiquid, neighborLiquidColumn, intervalTop);
                    if (Math.Abs(ownerTop - neighborTop) < 0.001f) return;

                    var useOwner = ownerTop > neighborTop;
                    var source = useOwner ? ownerLiquid : neighborLiquid;
                    var opposite = useOwner ? neighborLiquid : ownerLiquid;
                    var sourceSide = useOwner
                        ? segment.OwnerSide
                        : Opposite(segment.OwnerSide);
                    if (!blocks.TryGet(source.Material.BlockId, out var liquidBlock) ||
                        liquidBlock is null)
                        return;
                    EmitPatch(
                        source, opposite, liquidBlock, translucent: true, sourceSide,
                        Math.Max(intervalBottom, Math.Min(ownerTop, neighborTop)),
                        Math.Max(ownerTop, neighborTop), along, end);
                }

                static float LiquidTop(
                    TerrainLodColumnSpan span,
                    TerrainLodColumn column,
                    int intervalTop)
                {
                    var top = (float)intervalTop;
                    if (intervalTop == span.TopY && span.TopY < column.WorldHeight &&
                        column.At(span.TopY).IsAir)
                        top -= FluidMath.GetFluidHeightFromMeta(span.Material.Metadata);
                    return top;
                }
            }
        }

        var completedPages = pages.OrderBy(static pair => pair.Key.X)
            .ThenBy(static pair => pair.Key.Y)
            .ThenBy(static pair => pair.Key.Z)
            .Select(static pair => pair.Value.Build(pair.Key))
            .ToArray();
        if (Math.Max(segment.Owner.Tile.Level, segment.Neighbor?.Tile.Level ?? 0) >=
            TerrainLodSpatialMeshBuilder.TileScaleSubmissionMinimumLevel)
            completedPages = TerrainLodSpatialMeshBuilder.CoalescePages(completedPages);

        return new TerrainLodSpatialSeamMeshData(
            segment,
            ComputeCanonicalHash(segment, owner, neighbor, caveCullBelowY),
            completedPages);

        static void AddTransitions(SortedSet<int> target, TerrainLodColumn column)
        {
            foreach (var span in column.Spans)
            {
                target.Add(span.BottomY);
                target.Add(span.TopY);
            }
        }

        static int SurfaceTop(TerrainLodColumn column)
        {
            for (var index = column.Spans.Count - 1; index >= 0; index--)
            {
                var span = column.Spans[index];
                if (!span.IsAir) return span.TopY;
            }
            return 0;
        }

        TerrainLodColumn ColumnAt(
            TerrainLodColumnTile tile,
            int budget,
            int worldX,
            int worldZ)
        {
            var sample = checked(1 << tile.HorizontalSampleLevel);
            var localX = (int)((worldX - tile.Key.MinChunkX * 16) / sample);
            var localZ = (int)((worldZ - tile.Key.MinChunkZ * 16) / sample);
            if ((uint)localX >= (uint)tile.Width || (uint)localZ >= (uint)tile.Width)
                throw new InvalidDataException(
                    $"Spatial seam sample {worldX},{worldZ} lies outside {tile.Key}.");
            var key = (tile, localX, localZ, budget);
            if (!reduced.TryGetValue(key, out var column))
            {
                var source = caveCullBelowY is { } ceilingY
                    ? TerrainLodCaveCuller.SealUndergroundAir(
                        tile[localX, localZ], ceilingY)
                    : tile[localX, localZ];
                column = TerrainLodVerticalSliceReducer.Reduce(source, budget);
                reduced.Add(key, column);
            }
            return column;
        }

        void EmitPatch(
            TerrainLodColumnSpan span,
            TerrainLodColumnSpan? exposedNeighbor,
            Block block,
            bool translucent,
            TerrainLodSpatialBoundarySide boundarySide,
            float y0,
            float y1,
            int patchStart,
            int patchEnd)
        {
            var side = ToBlockSide(boundarySide);
            var tint = block.GetColorForFace(span.Material.Metadata, (int)side);
            var appearance = TerrainLodMeshBuilder.ResolveFaceAppearance(
                block, span.Material.Metadata, side, tint,
                ReferenceEquals(block, grassBlock), grassOverlayTexture);
            var shade = boundarySide is TerrainLodSpatialBoundarySide.West or
                TerrainLodSpatialBoundarySide.East ? 0.6f : 0.8f;
            var anchorX = boundarySide switch
            {
                TerrainLodSpatialBoundarySide.West => fixedBlock + 0.001,
                TerrainLodSpatialBoundarySide.East => fixedBlock - 0.001,
                _ => (patchStart + patchEnd) * 0.5
            };
            var anchorZ = boundarySide switch
            {
                TerrainLodSpatialBoundarySide.North => fixedBlock + 0.001,
                TerrainLodSpatialBoundarySide.South => fixedBlock - 0.001,
                _ => (patchStart + patchEnd) * 0.5
            };
            var page = PageFor(anchorX, (y0 + y1) * 0.5, anchorZ);
            var height = y1 - y0;
            var length = patchEnd - patchStart;
            var light = TerrainLodSpatialMeshBuilder.FaceLight(
                span, exposedNeighbor, side);
            switch (boundarySide)
            {
                case TerrainLodSpatialBoundarySide.West:
                    Add((fixedBlock, y1, patchStart), (fixedBlock, y0, patchStart),
                        (fixedBlock, y0, patchEnd), (fixedBlock, y1, patchEnd));
                    break;
                case TerrainLodSpatialBoundarySide.East:
                    Add((fixedBlock, y1, patchEnd), (fixedBlock, y0, patchEnd),
                        (fixedBlock, y0, patchStart), (fixedBlock, y1, patchStart));
                    break;
                case TerrainLodSpatialBoundarySide.North:
                    Add((patchEnd, y1, fixedBlock), (patchEnd, y0, fixedBlock),
                        (patchStart, y0, fixedBlock), (patchStart, y1, fixedBlock));
                    break;
                case TerrainLodSpatialBoundarySide.South:
                    Add((patchStart, y1, fixedBlock), (patchStart, y0, fixedBlock),
                        (patchEnd, y0, fixedBlock), (patchEnd, y1, fixedBlock));
                    break;
            }
            return;

            void Add(
                (float X, float Y, float Z) a,
                (float X, float Y, float Z) b,
                (float X, float Y, float Z) c,
                (float X, float Y, float Z) d) => TerrainLodSpatialMeshBuilder.Emit(
                page, translucent, side, appearance, shade,
                light, length, height, a, b, c, d);
        }

        TerrainLodSpatialMeshBuilder.PageBuilder PageFor(
            double worldX,
            double worldY,
            double worldZ)
        {
            var key = new TerrainLodSpatialMeshPageKey(
                TerrainLodSpatialMeshBuilder.FloorDivide(
                    (int)Math.Floor(worldX), TerrainLodSpatialMeshBuilder.PageSize),
                TerrainLodSpatialMeshBuilder.FloorDivide(
                    (int)Math.Floor(worldY), TerrainLodSpatialMeshBuilder.PageSize),
                TerrainLodSpatialMeshBuilder.FloorDivide(
                    (int)Math.Floor(worldZ), TerrainLodSpatialMeshBuilder.PageSize));
            if (!pages.TryGetValue(key, out var page))
            {
                page = new TerrainLodSpatialMeshBuilder.PageBuilder(
                    checked(key.X * TerrainLodSpatialMeshBuilder.PageSize),
                    checked(key.Y * TerrainLodSpatialMeshBuilder.PageSize),
                    checked(key.Z * TerrainLodSpatialMeshBuilder.PageSize));
                pages.Add(key, page);
            }
            return page;
        }
    }

    private static int CheckedBlock(long chunk) => checked((int)(chunk * 16));

    private static int SourceX(
        TerrainLodSpatialBoundarySide side,
        int fixedBlock,
        int along,
        bool ownerSide) => side switch
    {
        TerrainLodSpatialBoundarySide.West => ownerSide ? fixedBlock : fixedBlock - 1,
        TerrainLodSpatialBoundarySide.East => ownerSide ? fixedBlock - 1 : fixedBlock,
        _ => along
    };

    private static int SourceZ(
        TerrainLodSpatialBoundarySide side,
        int fixedBlock,
        int along,
        bool ownerSide) => side switch
    {
        TerrainLodSpatialBoundarySide.North => ownerSide ? fixedBlock : fixedBlock - 1,
        TerrainLodSpatialBoundarySide.South => ownerSide ? fixedBlock - 1 : fixedBlock,
        _ => along
    };

    private static TerrainLodSpatialBoundarySide Opposite(
        TerrainLodSpatialBoundarySide side) => side switch
    {
        TerrainLodSpatialBoundarySide.West => TerrainLodSpatialBoundarySide.East,
        TerrainLodSpatialBoundarySide.East => TerrainLodSpatialBoundarySide.West,
        TerrainLodSpatialBoundarySide.North => TerrainLodSpatialBoundarySide.South,
        TerrainLodSpatialBoundarySide.South => TerrainLodSpatialBoundarySide.North,
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    };

    private static Side ToBlockSide(TerrainLodSpatialBoundarySide side) => side switch
    {
        TerrainLodSpatialBoundarySide.West => Side.West,
        TerrainLodSpatialBoundarySide.East => Side.East,
        TerrainLodSpatialBoundarySide.North => Side.North,
        TerrainLodSpatialBoundarySide.South => Side.South,
        _ => throw new ArgumentOutOfRangeException(nameof(side))
    };

    internal static string ComputeCanonicalHash(
        TerrainLodSpatialSeamSegment segment,
        TerrainLodColumnTile owner,
        TerrainLodColumnTile? neighbor,
        int? caveCullBelowY = null)
    {
        var value = string.Join("|",
            owner.CanonicalHash,
            segment.Owner.MaximumVerticalSlices,
            neighbor?.CanonicalHash ?? "air",
            segment.Neighbor?.MaximumVerticalSlices ?? 0,
            (int)segment.OwnerSide,
            segment.FixedChunkCoordinate,
            segment.AlongStartChunk,
            segment.AlongEndChunk,
            caveCullBelowY?.ToString() ?? "none");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
