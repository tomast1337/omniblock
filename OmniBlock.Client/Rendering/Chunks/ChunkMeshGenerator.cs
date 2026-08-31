using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks;
using OmniBlock.Client.Rendering.Chunks.Occlusion;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Textures;
using OmniBlock.Util;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core;
using Microsoft.Extensions.Logging;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Chunks;

internal struct MeshBuildResult : IDisposable
{
    public PooledList<ChunkVertex> Solid;
    public PooledList<ChunkVertex> Translucent;
    public bool IsLit;
    public ChunkVisibilityStore VisibilityData;
    public Vector3D<int> Pos;
    public long Version;

    public readonly void Dispose()
    {
        Solid?.Dispose();
        Translucent?.Dispose();
    }
}

internal class ChunkMeshGenerator : IDisposable
{
    private readonly ILogger<ChunkMeshGenerator> _logger = Log.Instance.For<ChunkMeshGenerator>();

    private readonly ConcurrentQueue<MeshBuildResult> _results = new();
    private readonly ObjectPool<PooledList<ChunkVertex>> _listPool =
        new(() => new PooledList<ChunkVertex>(), 64);

    private SemaphoreSlim? _concurrencySemaphore;

    public ChunkMeshGenerator(ushort maxConcurrentTasks = 0)
    {
        MaxConcurrentTasks = maxConcurrentTasks;
    }

    public bool TryDequeueMesh(out MeshBuildResult result)
    {
        return _results.TryDequeue(out result);
    }

    public ushort MaxConcurrentTasks
    {
        get;
        set
        {
            field = value;

            _concurrencySemaphore?.Dispose();
            _concurrencySemaphore = field > 0
                ? new SemaphoreSlim(field, field)
                : null;
        }
    }

    //TODO: Make a chunk mesh config struct for alternateBlocks and other flags
    public void MeshChunk(World world, Vector3D<int> pos, long version, bool alternateBlocks)
    {
        // 1 block of padding on every side of the 16-block sub-chunk (18x18x18 total) — exactly
        // what face culling and AO need to look at a block's immediate neighbours.
        WorldRegionSnapshot cache = new(
            world,
            pos.X - 1, pos.Y - 1, pos.Z - 1,
            pos.X + SubChunkRenderer.Size, pos.Y + SubChunkRenderer.Size, pos.Z + SubChunkRenderer.Size
        );

        Task.Run(async () =>
        {
            if (_concurrencySemaphore != null)
                await _concurrencySemaphore.WaitAsync();

            try
            {
                MeshBuildResult mesh = GenerateMesh(pos, version, cache, alternateBlocks);
                _results.Enqueue(mesh);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating chunk mesh at {Pos}", pos);
            }
            finally
            {
                cache.Dispose();
                _concurrencySemaphore?.Release();
            }
        });
    }

    private MeshBuildResult GenerateMesh(Vector3D<int> pos, long version, WorldRegionSnapshot cache, bool alternateBlocks)
    {
        int minX = pos.X;
        int minY = pos.Y;
        int minZ = pos.Z;
        int maxX = pos.X + SubChunkRenderer.Size;
        int maxY = pos.Y + SubChunkRenderer.Size;
        int maxZ = pos.Z + SubChunkRenderer.Size;

        var result = new MeshBuildResult
        {
            Pos = pos,
            Version = version
        };

        var tess = new Tessellator();
        var ctx = new BlockRenderContext(cache, tess, cache);

        // Full 1x1x1 Standard blocks (minus grass, minus anything using texture variance) are
        // pulled out of the per-block loop below and merged into larger quads instead — see
        // EmitGreedyMesh. Precomputed once so the sweep and the loop's skip check agree on
        // exactly which cells were handled the fast way.
        Block?[] greedyEligible = new Block[SubChunkRenderer.Size * SubChunkRenderer.Size * SubChunkRenderer.Size];
        for (int y = minY; y < maxY; y++)
        {
            for (int z = minZ; z < maxZ; z++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    if (TryGetGreedyEligibleBlock(cache, x, y, z, alternateBlocks, out Block? eligible))
                    {
                        greedyEligible[LocalIndex(x - minX, y - minY, z - minZ)] = eligible;
                    }
                }
            }
        }

        for (int pass = 0; pass < 2; pass++)
        {
            bool hasNextPass = false;

            tess.startCapture(TesselatorCaptureVertexFormat.Chunk);
            tess.startDrawingQuads();
            tess.setTranslationD(-pos.X, -pos.Y, -pos.Z);

            if (pass == 0)
            {
                EmitGreedyMesh(cache, ctx, tess, greedyEligible, minX, minY, minZ);
            }

            for (int y = minY; y < maxY; y++)
            {
                for (int z = minZ; z < maxZ; z++)
                {
                    for (int x = minX; x < maxX; x++)
                    {
                        int id = cache.GetBlockId(x, y, z);
                        if (id <= 0) continue;

                        Block b = BlockRegistry.GetByProtocolId(id);
                        int blockPass = b.RenderLayer;

                        if (blockPass != pass)
                        {
                            hasNextPass = true;
                        }
                        else if (pass != 0 || greedyEligible[LocalIndex(x - minX, y - minY, z - minZ)] is null)
                        {
                            BlockRenderer.RenderBlockByRenderType(cache, cache, b, new BlockPos(x, y, z), tess, doVariance: alternateBlocks);
                        }
                    }
                }
            }

            tess.draw(ProgramSlot.Terrain);
            tess.setTranslationD(0, 0, 0);

            PooledList<ChunkVertex> verts = tess.endCaptureChunkVertices();
            if (verts.Count > 0)
            {
                PooledList<ChunkVertex> list = _listPool.Get();
                list.AddRange(verts.Span);

                if (pass == 0)
                {
                    result.Solid = list;
                }
                else
                {
                    result.Translucent = list;
                }
            }

            if (!hasNextPass) break;
        }

        result.IsLit = cache.IsLit;
        result.VisibilityData = ChunkVisibilityComputer.Compute(cache, pos.X, pos.Y, pos.Z);
        return result;
    }

    private const float ColorScale = 0.0039215686F;
    private const float TopShadow = 1.0F;
    private const float BottomShadow = 0.5F;
    private const float EastShadow = 0.8F;
    private const float WestShadow = 0.8F;
    private const float NorthShadow = 0.6F;
    private const float SouthShadow = 0.6F;

    /// <summary>
    ///     Grass draws a second, biome-tinted overlay quad over its side texture — a shape a single
    ///     merged quad can't represent — so any block using this texture is excluded from the greedy
    ///     path entirely and falls back to the ordinary per-block draw.
    /// </summary>
    private static readonly int s_grassSideTextureId = Atlases.Terrain.IndexOf("omniblock:grass_block_side");

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int LocalIndex(int lx, int ly, int lz) => (lx * SubChunkRenderer.Size + lz) * SubChunkRenderer.Size + ly;

    /// <summary>
    ///     Whether the block at this cell can take the greedy-meshing fast path: a full 1x1x1
    ///     <see cref="BlockRendererType.Standard" /> block, not grass, and — when texture variance is
    ///     active for this mesh — not using it. Any of those disqualify it because they make two
    ///     otherwise-identical adjacent faces render differently (a partial bounding box changes the
    ///     quad's shape; variance and grass both change which texture/orientation a face gets), which
    ///     a single merged quad can't reproduce.
    /// </summary>
    private static bool TryGetGreedyEligibleBlock(WorldRegionSnapshot cache, int x, int y, int z, bool alternateBlocks, out Block? block)
    {
        int id = cache.GetBlockId(x, y, z);
        if (id <= 0)
        {
            block = null;
            return false;
        }

        Block candidate = BlockRegistry.GetByProtocolId(id);
        if (candidate.RenderType != BlockRendererType.Standard || candidate.RenderLayer != 0)
        {
            block = null;
            return false;
        }

        candidate.UpdateBoundingBox(cache, x, y, z);
        Box bb = candidate.BoundingBox;
        if (bb.MinX != 0.0 || bb.MinY != 0.0 || bb.MinZ != 0.0 || bb.MaxX != 1.0 || bb.MaxY != 1.0 || bb.MaxZ != 1.0)
        {
            block = null;
            return false;
        }

        if (alternateBlocks &&
            (candidate.TopVariance != TextureVariance.None ||
             candidate.BottomVariance != TextureVariance.None ||
             candidate.SideVariance != TextureVariance.None))
        {
            block = null;
            return false;
        }

        if (candidate.GetTextureId(cache, x, y, z, Side.North) == s_grassSideTextureId)
        {
            block = null;
            return false;
        }

        block = candidate;
        return true;
    }

    /// <summary>One corner of a quad about to be emitted: world position, tiled UV, and its light.</summary>
    private readonly record struct QuadCorner(float X, float Y, float Z, float U, float V, CornerLight Light);

    /// <summary>
    ///     What two faces must agree on, bit for bit, to be merged: the same texture, the same tint,
    ///     and identical lighting at all four corners. Requiring the whole tuple rather than just the
    ///     shared edge is conservative — it merges less than a fully general algorithm would across a
    ///     lighting gradient — but it means a merged quad's corners are exactly the value every
    ///     contributing cell already agreed on, with no interpolation to get wrong.
    /// </summary>
    private readonly record struct FaceMergeKey(int ArrayLayer, int TintColor, CornerLight L0, CornerLight L1, CornerLight L2, CornerLight L3);

    private static void EmitGreedyMesh(WorldRegionSnapshot cache, BlockRenderContext ctx, Tessellator tess, Block?[] eligible, int minX, int minY, int minZ)
    {
        EmitGreedyTop(cache, ctx, tess, eligible, minX, minY, minZ);
        EmitGreedyBottom(cache, ctx, tess, eligible, minX, minY, minZ);
        EmitGreedyEast(cache, ctx, tess, eligible, minX, minY, minZ);
        EmitGreedyWest(cache, ctx, tess, eligible, minX, minY, minZ);
        EmitGreedyNorth(cache, ctx, tess, eligible, minX, minY, minZ);
        EmitGreedySouth(cache, ctx, tess, eligible, minX, minY, minZ);
    }

    private static void EmitGreedyTop(WorldRegionSnapshot cache, BlockRenderContext ctx, Tessellator tess, Block?[] eligible, int minX, int minY, int minZ)
    {
        int size = SubChunkRenderer.Size;
        FaceMergeKey?[] grid = new FaceMergeKey?[size * size];
        List<(int U, int V, int W, int H, FaceMergeKey Key)> rects = [];

        for (int depth = 0; depth < size; depth++)
        {
            int y = minY + depth;
            Array.Clear(grid);

            for (int v = 0; v < size; v++)
            {
                int z = minZ + v;
                for (int u = 0; u < size; u++)
                {
                    int x = minX + u;
                    if (eligible[LocalIndex(u, depth, v)] is not { } block) continue;
                    if (!block.IsSideVisible(cache, x, y + 1, z, Side.Up)) continue;

                    var (v0, v1, v2, v3, _) = ctx.ComputeTopFaceLight(block, new BlockPos(x, y, z));
                    int textureId = block.GetTextureId(cache, x, y, z, Side.Up);
                    int layer = Atlases.Terrain.LayerOfGridIndex(textureId);
                    int tint = block.GetColorMultiplier(cache, x, y, z);

                    grid[v * size + u] = new FaceMergeKey(layer, tint, v0, v1, v2, v3);
                }
            }

            rects.Clear();
            GreedyMergeLayer(grid, size, rects);

            foreach (var (u0, v0i, w, h, key) in rects)
            {
                float x0 = minX + u0, x1 = x0 + w;
                float z0 = minZ + v0i, z1 = z0 + h;
                float yFace = y + 1;

                QuadCorner tl = new(x1, yFace, z1, w, h, key.L0);
                QuadCorner bl = new(x1, yFace, z0, w, 0, key.L1);
                QuadCorner br = new(x0, yFace, z0, 0, 0, key.L2);
                QuadCorner tr = new(x0, yFace, z1, 0, h, key.L3);

                bool flipped = key.L0.FlipWeight + key.L2.FlipWeight > key.L1.FlipWeight + key.L3.FlipWeight;
                EmitMergedQuad(tess, key, flipped, TopShadow, tl, bl, br, tr);
            }
        }
    }

    private static void EmitGreedyBottom(WorldRegionSnapshot cache, BlockRenderContext ctx, Tessellator tess, Block?[] eligible, int minX, int minY, int minZ)
    {
        int size = SubChunkRenderer.Size;
        FaceMergeKey?[] grid = new FaceMergeKey?[size * size];
        List<(int U, int V, int W, int H, FaceMergeKey Key)> rects = [];

        for (int depth = 0; depth < size; depth++)
        {
            int y = minY + depth;
            Array.Clear(grid);

            for (int v = 0; v < size; v++)
            {
                int z = minZ + v;
                for (int u = 0; u < size; u++)
                {
                    int x = minX + u;
                    if (eligible[LocalIndex(u, depth, v)] is not { } block) continue;
                    if (!block.IsSideVisible(cache, x, y - 1, z, Side.Down)) continue;

                    var (v0, v1, v2, v3, _) = ctx.ComputeBottomFaceLight(block, new BlockPos(x, y, z));
                    int textureId = block.GetTextureId(cache, x, y, z, Side.Down);
                    int layer = Atlases.Terrain.LayerOfGridIndex(textureId);
                    int tint = block.TextureId != 3 ? block.GetColorMultiplier(cache, x, y, z) : 0xFFFFFF;

                    grid[v * size + u] = new FaceMergeKey(layer, tint, v0, v1, v2, v3);
                }
            }

            rects.Clear();
            GreedyMergeLayer(grid, size, rects);

            foreach (var (u0, v0i, w, h, key) in rects)
            {
                float x0 = minX + u0, x1 = x0 + w;
                float z0 = minZ + v0i, z1 = z0 + h;
                float yFace = y;

                QuadCorner tl = new(x0, yFace, z1, 0, h, key.L0);
                QuadCorner bl = new(x0, yFace, z0, 0, 0, key.L1);
                QuadCorner br = new(x1, yFace, z0, w, 0, key.L2);
                QuadCorner tr = new(x1, yFace, z1, w, h, key.L3);

                bool flipped = key.L0.FlipWeight + key.L2.FlipWeight > key.L1.FlipWeight + key.L3.FlipWeight;
                EmitMergedQuad(tess, key, flipped, BottomShadow, tl, bl, br, tr);
            }
        }
    }

    private static void EmitGreedyEast(WorldRegionSnapshot cache, BlockRenderContext ctx, Tessellator tess, Block?[] eligible, int minX, int minY, int minZ)
    {
        int size = SubChunkRenderer.Size;
        FaceMergeKey?[] grid = new FaceMergeKey?[size * size];
        List<(int U, int V, int W, int H, FaceMergeKey Key)> rects = [];

        for (int depth = 0; depth < size; depth++)
        {
            int z = minZ + depth;
            Array.Clear(grid);

            for (int v = 0; v < size; v++)
            {
                int y = minY + v;
                for (int u = 0; u < size; u++)
                {
                    int x = minX + u;
                    if (eligible[LocalIndex(u, v, depth)] is not { } block) continue;
                    if (!block.IsSideVisible(cache, x, y, z - 1, Side.North)) continue;

                    var (v0, v1, v2, v3, _) = ctx.ComputeEastFaceLight(block, new BlockPos(x, y, z));
                    int textureId = block.GetTextureId(cache, x, y, z, Side.North);
                    if (textureId == s_grassSideTextureId) continue;
                    int layer = Atlases.Terrain.LayerOfGridIndex(textureId);
                    int tint = block.TextureId != 3 ? block.GetColorMultiplier(cache, x, y, z) : 0xFFFFFF;

                    grid[v * size + u] = new FaceMergeKey(layer, tint, v0, v1, v2, v3);
                }
            }

            rects.Clear();
            GreedyMergeLayer(grid, size, rects);

            foreach (var (u0, v0i, w, h, key) in rects)
            {
                float x0 = minX + u0, x1 = x0 + w;
                float y0 = minY + v0i, y1 = y0 + h;
                float zFace = z;

                // DrawEastFace's TopLeft/BottomLeft/BottomRight/TopRight land on v1/v2/v3/v0 (see
                // DrawBlock's EAST FACE branch: AssignVertexColors(v1, v2, v3, v0, ...)) — one step
                // rotated from the raw quadrant order the FaceMergeKey stores its four corners in.
                QuadCorner tl = new(x1, y1, zFace, 0, 0, key.L1);
                QuadCorner bl = new(x1, y0, zFace, 0, h, key.L2);
                QuadCorner br = new(x0, y0, zFace, w, h, key.L3);
                QuadCorner tr = new(x0, y1, zFace, w, 0, key.L0);

                bool flipped = key.L1.FlipWeight + key.L3.FlipWeight > key.L2.FlipWeight + key.L0.FlipWeight;
                EmitMergedQuad(tess, key, flipped, EastShadow, tl, bl, br, tr);
            }
        }
    }

    private static void EmitGreedyWest(WorldRegionSnapshot cache, BlockRenderContext ctx, Tessellator tess, Block?[] eligible, int minX, int minY, int minZ)
    {
        int size = SubChunkRenderer.Size;
        FaceMergeKey?[] grid = new FaceMergeKey?[size * size];
        List<(int U, int V, int W, int H, FaceMergeKey Key)> rects = [];

        for (int depth = 0; depth < size; depth++)
        {
            int z = minZ + depth;
            Array.Clear(grid);

            for (int v = 0; v < size; v++)
            {
                int y = minY + v;
                for (int u = 0; u < size; u++)
                {
                    int x = minX + u;
                    if (eligible[LocalIndex(u, v, depth)] is not { } block) continue;
                    if (!block.IsSideVisible(cache, x, y, z + 1, Side.South)) continue;

                    var (v0, v1, v2, v3, _) = ctx.ComputeWestFaceLight(block, new BlockPos(x, y, z));
                    int textureId = block.GetTextureId(cache, x, y, z, Side.South);
                    if (textureId == s_grassSideTextureId) continue;
                    int layer = Atlases.Terrain.LayerOfGridIndex(textureId);
                    int tint = block.TextureId != 3 ? block.GetColorMultiplier(cache, x, y, z) : 0xFFFFFF;

                    grid[v * size + u] = new FaceMergeKey(layer, tint, v0, v1, v2, v3);
                }
            }

            rects.Clear();
            GreedyMergeLayer(grid, size, rects);

            foreach (var (u0, v0i, w, h, key) in rects)
            {
                float x0 = minX + u0, x1 = x0 + w;
                float y0 = minY + v0i, y1 = y0 + h;
                float zFace = z + 1;

                QuadCorner tl = new(x0, y1, zFace, 0, 0, key.L0);
                QuadCorner bl = new(x0, y0, zFace, 0, h, key.L1);
                QuadCorner br = new(x1, y0, zFace, w, h, key.L2);
                QuadCorner tr = new(x1, y1, zFace, w, 0, key.L3);

                bool flipped = key.L0.FlipWeight + key.L2.FlipWeight > key.L1.FlipWeight + key.L3.FlipWeight;
                EmitMergedQuad(tess, key, flipped, WestShadow, tl, bl, br, tr);
            }
        }
    }

    private static void EmitGreedyNorth(WorldRegionSnapshot cache, BlockRenderContext ctx, Tessellator tess, Block?[] eligible, int minX, int minY, int minZ)
    {
        int size = SubChunkRenderer.Size;
        FaceMergeKey?[] grid = new FaceMergeKey?[size * size];
        List<(int U, int V, int W, int H, FaceMergeKey Key)> rects = [];

        for (int depth = 0; depth < size; depth++)
        {
            int x = minX + depth;
            Array.Clear(grid);

            for (int v = 0; v < size; v++)
            {
                int y = minY + v;
                for (int u = 0; u < size; u++)
                {
                    int z = minZ + u;
                    if (eligible[LocalIndex(depth, v, u)] is not { } block) continue;
                    if (!block.IsSideVisible(cache, x - 1, y, z, Side.West)) continue;

                    var (v0, v1, v2, v3, _) = ctx.ComputeNorthFaceLight(block, new BlockPos(x, y, z));
                    int textureId = block.GetTextureId(cache, x, y, z, Side.West);
                    if (textureId == s_grassSideTextureId) continue;
                    int layer = Atlases.Terrain.LayerOfGridIndex(textureId);
                    int tint = block.TextureId != 3 ? block.GetColorMultiplier(cache, x, y, z) : 0xFFFFFF;

                    grid[v * size + u] = new FaceMergeKey(layer, tint, v0, v1, v2, v3);
                }
            }

            rects.Clear();
            GreedyMergeLayer(grid, size, rects);

            foreach (var (u0, v0i, w, h, key) in rects)
            {
                float z0 = minZ + u0, z1 = z0 + w;
                float y0 = minY + v0i, y1 = y0 + h;
                float xFace = x;

                // Same one-step rotation as EmitGreedyEast — DrawNorthFace's corners land on
                // v1/v2/v3/v0 (DrawBlock's NORTH FACE: AssignVertexColors(v1, v2, v3, v0, ...)).
                QuadCorner tl = new(xFace, y1, z0, 0, 0, key.L1);
                QuadCorner bl = new(xFace, y0, z0, 0, h, key.L2);
                QuadCorner br = new(xFace, y0, z1, w, h, key.L3);
                QuadCorner tr = new(xFace, y1, z1, w, 0, key.L0);

                bool flipped = key.L1.FlipWeight + key.L3.FlipWeight > key.L2.FlipWeight + key.L0.FlipWeight;
                EmitMergedQuad(tess, key, flipped, NorthShadow, tl, bl, br, tr);
            }
        }
    }

    private static void EmitGreedySouth(WorldRegionSnapshot cache, BlockRenderContext ctx, Tessellator tess, Block?[] eligible, int minX, int minY, int minZ)
    {
        int size = SubChunkRenderer.Size;
        FaceMergeKey?[] grid = new FaceMergeKey?[size * size];
        List<(int U, int V, int W, int H, FaceMergeKey Key)> rects = [];

        for (int depth = 0; depth < size; depth++)
        {
            int x = minX + depth;
            Array.Clear(grid);

            for (int v = 0; v < size; v++)
            {
                int y = minY + v;
                for (int u = 0; u < size; u++)
                {
                    int z = minZ + u;
                    if (eligible[LocalIndex(depth, v, u)] is not { } block) continue;
                    if (!block.IsSideVisible(cache, x + 1, y, z, Side.East)) continue;

                    var (v0, v1, v2, v3, _) = ctx.ComputeSouthFaceLight(block, new BlockPos(x, y, z));
                    int textureId = block.GetTextureId(cache, x, y, z, Side.East);
                    if (textureId == s_grassSideTextureId) continue;
                    int layer = Atlases.Terrain.LayerOfGridIndex(textureId);
                    int tint = block.TextureId != 3 ? block.GetColorMultiplier(cache, x, y, z) : 0xFFFFFF;

                    grid[v * size + u] = new FaceMergeKey(layer, tint, v0, v1, v2, v3);
                }
            }

            rects.Clear();
            GreedyMergeLayer(grid, size, rects);

            foreach (var (u0, v0i, w, h, key) in rects)
            {
                float z0 = minZ + u0, z1 = z0 + w;
                float y0 = minY + v0i, y1 = y0 + h;
                float xFace = x + 1;

                // DrawSouthFace's corners land on v3/v0/v1/v2 (DrawBlock's SOUTH FACE:
                // AssignVertexColors(v3, v0, v1, v2, ...)) — a different rotation from East/North.
                QuadCorner tl = new(xFace, y1, z1, 0, 0, key.L3);
                QuadCorner bl = new(xFace, y0, z1, 0, h, key.L0);
                QuadCorner br = new(xFace, y0, z0, w, h, key.L1);
                QuadCorner tr = new(xFace, y1, z0, w, 0, key.L2);

                bool flipped = key.L3.FlipWeight + key.L1.FlipWeight > key.L0.FlipWeight + key.L2.FlipWeight;
                EmitMergedQuad(tess, key, flipped, SouthShadow, tl, bl, br, tr);
            }
        }
    }

    /// <summary>
    ///     Standard 2D greedy-rectangle merge over one depth layer: from each unconsumed cell, grow a
    ///     rectangle as wide as the matching run to its right, then as tall as that whole width stays
    ///     matching going down. Consumed cells are nulled out of <paramref name="grid" /> in place, so
    ///     no separate visited mask is needed.
    /// </summary>
    private static void GreedyMergeLayer(FaceMergeKey?[] grid, int size, List<(int U, int V, int W, int H, FaceMergeKey Key)> rects)
    {
        for (int v = 0; v < size; v++)
        {
            for (int u = 0; u < size; u++)
            {
                int idx = v * size + u;
                if (grid[idx] is not { } key) continue;

                int w = 1;
                while (u + w < size && grid[v * size + u + w] is { } wk && wk.Equals(key)) w++;

                int h = 1;
                bool canExpand = true;
                while (canExpand && v + h < size)
                {
                    for (int du = 0; du < w; du++)
                    {
                        if (grid[(v + h) * size + u + du] is not { } hk || !hk.Equals(key))
                        {
                            canExpand = false;
                            break;
                        }
                    }

                    if (canExpand) h++;
                }

                for (int dv = 0; dv < h; dv++)
                {
                    for (int du = 0; du < w; du++)
                    {
                        grid[(v + dv) * size + u + du] = null;
                    }
                }

                rects.Add((u, v, w, h, key));
            }
        }
    }

    /// <summary>
    ///     Emits one quad in the canonical top-left/bottom-left/bottom-right/top-right winding, or
    ///     that same cycle started one vertex later when <paramref name="flipped" /> — which is all
    ///     "flipped" ever means in the per-block renderer this mirrors: which diagonal the two
    ///     triangles split along, not a different set of corners.
    /// </summary>
    private static void EmitMergedQuad(Tessellator tess, in FaceMergeKey key, bool flipped, float shade, QuadCorner tl, QuadCorner bl, QuadCorner br, QuadCorner tr)
    {
        float r = (key.TintColor >> 16 & 255) * ColorScale * shade;
        float g = (key.TintColor >> 8 & 255) * ColorScale * shade;
        float b = (key.TintColor & 255) * ColorScale * shade;

        tess.setArrayLayer(key.ArrayLayer);

        Span<QuadCorner> corners = [tl, bl, br, tr];
        int start = flipped ? 1 : 0;
        for (int i = 0; i < 4; i++)
        {
            QuadCorner c = corners[(start + i) % 4];
            tess.setColorOpaque_F(r, g, b);
            tess.setLight(c.Light.Sky, c.Light.Block);
            tess.addVertexWithUV(c.X, c.Y, c.Z, c.U, c.V);
        }
    }

    public void Dispose()
    {
        _listPool.Dispose();
    }
}
