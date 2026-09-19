using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Registries;
using OmniBlock.Textures;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

/// <summary>CPU geometry for one detail level of one distant chunk column.</summary>
internal sealed record TerrainLodMeshData(
    int Level,
    ChunkVertex[] Vertices,
    ChunkLightVertex[] Lights,
    ChunkVertex[] TranslucentVertices,
    ChunkLightVertex[] TranslucentLights)
{
    public long EstimatedBytes =>
        (long)Vertices.Length * WgpuMesh.ChunkVertexStride +
        (long)Lights.Length * WgpuMesh.ChunkLightVertexStride +
        (long)TranslucentVertices.Length * WgpuMesh.ChunkVertexStride +
        (long)TranslucentLights.Length * WgpuMesh.ChunkLightVertexStride;
}

internal readonly record struct TerrainLodFaceAppearance(
    int Texture,
    int Tint,
    int OverlayTexture = -1,
    int OverlayTint = 0xFFFFFF);

/// <summary>
///     Four compact edge planes retained after a hierarchy is uploaded. Material values are
///     palette-backed, so seam evidence costs O(surface) small indices instead of retaining the
///     O(volume) hierarchy or repeating resource-location strings per boundary cell.
/// </summary>
internal sealed class TerrainLodBoundarySummary
{
    private readonly TerrainLodMaterial[] _palette;
    private readonly Dictionary<int, BoundaryLevel> _levels;

    private TerrainLodBoundarySummary(
        int chunkX,
        int chunkZ,
        long terrainRevision,
        int minimumLevel,
        TerrainLodMaterial[] palette,
        Dictionary<int, BoundaryLevel> levels)
    {
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        TerrainRevision = terrainRevision;
        MinimumLevel = minimumLevel;
        _palette = palette;
        _levels = levels;
        EstimatedBytes = palette.Length * 32L + levels.Values.Sum(static level =>
            (long)(level.North.Length + level.South.Length +
                   level.West.Length + level.East.Length) * 4);
    }

    public int ChunkX { get; }
    public int ChunkZ { get; }
    public long TerrainRevision { get; }
    public int MinimumLevel { get; }
    public long EstimatedBytes { get; }
    public TerrainLodBoundaryIdentity Identity => new(TerrainRevision, MinimumLevel);
    public bool HasLevel(int level) => _levels.ContainsKey(level);
    public int NearestLevel(int requested) => _levels.Keys
        .OrderBy(level => Math.Abs(level - requested))
        .ThenBy(static level => level)
        .First();

    public static TerrainLodBoundarySummary Capture(
        TerrainLodHierarchy hierarchy, int minimumLevel, int maximumLevel)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        List<TerrainLodMaterial> palette = [TerrainLodMaterial.Air];
        Dictionary<TerrainLodMaterial, ushort> paletteIndices = [];
        Dictionary<int, BoundaryLevel> levels = [];
        var maximum = Math.Min(maximumLevel, hierarchy.Levels.Count - 1);
        for (var levelIndex = Math.Max(0, minimumLevel); levelIndex <= maximum; levelIndex++)
        {
            var level = hierarchy.Levels[levelIndex];
            var north = new BoundarySample[level.Width * level.Height];
            var south = new BoundarySample[level.Width * level.Height];
            var west = new BoundarySample[level.Depth * level.Height];
            var east = new BoundarySample[level.Depth * level.Height];
            for (var x = 0; x < level.Width; x++)
            for (var y = 0; y < level.Height; y++)
            {
                north[x * level.Height + y] = Sample(level[x, y, 0]);
                south[x * level.Height + y] = Sample(level[x, y, level.Depth - 1]);
            }
            for (var z = 0; z < level.Depth; z++)
            for (var y = 0; y < level.Height; y++)
            {
                west[z * level.Height + y] = Sample(level[0, y, z]);
                east[z * level.Height + y] = Sample(level[level.Width - 1, y, z]);
            }
            levels.Add(levelIndex, new BoundaryLevel(
                level.Width, level.Height, level.Depth, north, south, west, east));
        }

        return new TerrainLodBoundarySummary(
            hierarchy.ChunkX, hierarchy.ChunkZ, hierarchy.TerrainRevision,
            Math.Max(0, minimumLevel),
            [.. palette], levels);

        BoundarySample Sample(TerrainLodCell cell)
        {
            var solid = TerrainLodMeshBuilder.TrySelectVolumetricDepthMaterial(cell, out var depth)
                ? Index(depth)
                : (ushort)0;
            var translucent = TerrainLodMeshBuilder.TrySelectTranslucentMaterial(
                    cell, out var transparent)
                ? Index(transparent)
                : (ushort)0;
            return new BoundarySample(solid, translucent);
        }

        ushort Index(TerrainLodMaterial material)
        {
            if (paletteIndices.TryGetValue(material, out var index)) return index;
            if (palette.Count > ushort.MaxValue)
                throw new InvalidDataException("Terrain LOD boundary material palette is too large.");
            index = checked((ushort)palette.Count);
            palette.Add(material);
            paletteIndices.Add(material, index);
            return index;
        }
    }

    public bool TryGet(
        int level,
        Side side,
        int along,
        int y,
        bool translucent,
        out TerrainLodMaterial material)
    {
        material = TerrainLodMaterial.Air;
        if (!_levels.TryGetValue(level, out var boundary))
        {
            // A fine presentation may arrive before its neighbor has installed the same tier.
            // Query the coarsest covering boundary cell instead of treating that known neighbor
            // as missing. The fine caller still emits fine-sized patches, so no T-junction-sized
            // hole is introduced while the neighbor's detail upgrade is pending.
            var coveringLevel = _levels.Keys
                .Where(candidate => candidate > level)
                .OrderBy(candidate => candidate)
                .FirstOrDefault(-1);
            if (coveringLevel < 0) return false;
            boundary = _levels[coveringLevel];
            var requestedScale = 1 << level;
            var coveringScale = 1 << coveringLevel;
            along = along * requestedScale / coveringScale;
            y = y * requestedScale / coveringScale;
        }
        if (y < 0 || y >= boundary.Height) return false;
        BoundarySample sample;
        switch (side)
        {
            case Side.North when (uint)along < (uint)boundary.Width:
                sample = boundary.North[along * boundary.Height + y];
                break;
            case Side.South when (uint)along < (uint)boundary.Width:
                sample = boundary.South[along * boundary.Height + y];
                break;
            case Side.West when (uint)along < (uint)boundary.Depth:
                sample = boundary.West[along * boundary.Height + y];
                break;
            case Side.East when (uint)along < (uint)boundary.Depth:
                sample = boundary.East[along * boundary.Height + y];
                break;
            default:
                return false;
        }

        material = _palette[translucent ? sample.Translucent : sample.Solid];
        return true;
    }

    private readonly record struct BoundarySample(ushort Solid, ushort Translucent);
    private sealed record BoundaryLevel(
        int Width,
        int Height,
        int Depth,
        BoundarySample[] North,
        BoundarySample[] South,
        BoundarySample[] West,
        BoundarySample[] East);
}

internal readonly record struct TerrainLodBoundaryIdentity(long TerrainRevision, int MinimumLevel);

/// <summary>
///     Compiles the resource-pack-independent hierarchy into independent depth-writing and
///     translucent terrain-array quad streams. Keeping the streams separate lets the renderer retain
///     ordinary depth semantics and order blended distant columns back to front.
/// </summary>
internal static class TerrainLodMeshBuilder
{
    private static readonly float VerticalOrigin = ChuckFormat.WorldHeight / 2.0f;

    public static TerrainLodMeshData Build(
        TerrainLodHierarchy hierarchy,
        int levelIndex,
        IBlockRuntimeView blocks,
        bool hasSkyLight,
        ILightProvider? lighting = null,
        IBlockReader? visuals = null,
        int? caveCullBelowY = null)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        ArgumentNullException.ThrowIfNull(blocks);
        if ((uint)levelIndex >= (uint)hierarchy.Levels.Count)
            throw new ArgumentOutOfRangeException(nameof(levelIndex));

        var solid = BuildLayer(
            hierarchy, levelIndex, blocks, hasSkyLight, lighting, visuals,
            caveCullBelowY, false);
        var translucent = BuildLayer(
            hierarchy, levelIndex, blocks, hasSkyLight, lighting, visuals,
            caveCullBelowY, true);
        return new TerrainLodMeshData(
            levelIndex,
            solid.Vertices,
            solid.Lights,
            translucent.Vertices,
            translucent.Lights);
    }

    private static TerrainLodLayerData BuildLayer(
        TerrainLodHierarchy hierarchy,
        int levelIndex,
        IBlockRuntimeView blocks,
        bool hasSkyLight,
        ILightProvider? lighting,
        IBlockReader? visuals,
        int? caveCullBelowY,
        bool translucent)
    {

        var level = hierarchy.Levels[levelIndex];
        List<ChunkVertex> vertices = [];
        List<ChunkLightVertex> lights = [];
        var fallbackLight = hasSkyLight
            ? new ChunkLightVertex(ChunkVertexHelper.ToQuarterLevels(15), 0)
            : new ChunkLightVertex(0, 0);
        blocks.TryGet("omniblock:grass_block", out var grassBlock);
        var grassOverlayTexture = grassBlock is null
            ? -1
            : Atlases.Terrain.IndexOf("omniblock:grass_block_side_overlay");

        for (var x = 0; x < level.Width; x++)
        for (var z = 0; z < level.Depth; z++)
        for (var y = 0; y < level.Height; y++)
        {
            var cell = level[x, y, z];
            TerrainLodMaterial material;
            if (cell.IsEmpty) continue;
            var selected = translucent
                ? TrySelectTranslucentMaterial(cell, out material)
                : levelIndex == 0
                    ? TrySelectDepthMaterial(cell, out material)
                    : TrySelectVolumetricDepthMaterial(cell, out material);
            if (!selected) continue;
            if (!blocks.TryGet(material.BlockId, out var block) || block is null) continue;

            float minX = x * level.Scale;
            var minY = y * level.Scale - VerticalOrigin;
            float minZ = z * level.Scale;
            float maxX = Math.Min((x + 1) * level.Scale, 16);
            var maxY = Math.Min((y + 1) * level.Scale, ChuckFormat.WorldHeight) - VerticalOrigin;
            float maxZ = Math.Min((z + 1) * level.Scale, 16);
            if (levelIndex == 0 && material.Geometry is
                    TerrainLodGeometryClass.SurfaceLayer or TerrainLodGeometryClass.BoundedCube)
            {
                if (visuals is not null)
                    block.UpdateBoundingBox(
                        visuals,
                        hierarchy.ChunkX * 16 + x,
                        y,
                        hierarchy.ChunkZ * 16 + z);
                var bounds = block.BoundingBox;
                minX += (float)bounds.MinX;
                minZ += (float)bounds.MinZ;
                maxX = x + (float)bounds.MaxX;
                maxZ = z + (float)bounds.MaxZ;
                var cellBottom = y - VerticalOrigin;
                minY = cellBottom + (float)bounds.MinY;
                maxY = cellBottom + (float)bounds.MaxY;
            }

            if (!translucent && levelIndex == 0 &&
                material.Geometry == TerrainLodGeometryClass.CrossedQuad)
            {
                EmitCrossedQuad();
                continue;
            }
            // The ordinary fluid renderer lowers an exposed surface according to its level. The
            // coarse cell stays axis-aligned, but retaining that height prevents distant water
            // and lava from becoming a stack of completely full cubes.
            var renderMaxY = maxY;
            if (material.Geometry == TerrainLodGeometryClass.Liquid &&
                (cell.ExposedFaces & TerrainLodFaceMask.Up) != 0)
                renderMaxY -= FluidMath.GetFluidHeightFromMeta(material.Metadata);
            var tileX = maxX - minX;
            var tileY = renderMaxY - minY;
            var tileZ = maxZ - minZ;

            if ((cell.ExposedFaces & TerrainLodFaceMask.Down) != 0 &&
                !CullUndergroundFace(Side.Down))
                AddFace(Side.Down, 0.5f, tileX, tileZ,
                    (minX, minY, maxZ), (minX, minY, minZ),
                    (maxX, minY, minZ), (maxX, minY, maxZ));
            if ((cell.ExposedFaces & TerrainLodFaceMask.Up) != 0 &&
                !CullUndergroundFace(Side.Up))
                AddFace(Side.Up, 1.0f, tileX, tileZ,
                    (maxX, renderMaxY, maxZ), (maxX, renderMaxY, minZ),
                    (minX, renderMaxY, minZ), (minX, renderMaxY, maxZ));
            if ((cell.ExposedFaces & TerrainLodFaceMask.West) != 0 &&
                x > 0 && ShouldEmitLiquidSide(x - 1, y, z) &&
                !CullUndergroundFace(Side.West))
                AddFace(Side.West, 0.6f, tileZ, tileY,
                    (minX, renderMaxY, minZ), (minX, minY, minZ),
                    (minX, minY, maxZ), (minX, renderMaxY, maxZ));
            if ((cell.ExposedFaces & TerrainLodFaceMask.East) != 0 &&
                x < level.Width - 1 && ShouldEmitLiquidSide(x + 1, y, z) &&
                !CullUndergroundFace(Side.East))
                AddFace(Side.East, 0.6f, tileZ, tileY,
                    (maxX, renderMaxY, maxZ), (maxX, minY, maxZ),
                    (maxX, minY, minZ), (maxX, renderMaxY, minZ));
            if ((cell.ExposedFaces & TerrainLodFaceMask.North) != 0 &&
                z > 0 && ShouldEmitLiquidSide(x, y, z - 1) &&
                !CullUndergroundFace(Side.North))
                AddFace(Side.North, 0.8f, tileX, tileY,
                    (maxX, renderMaxY, minZ), (maxX, minY, minZ),
                    (minX, minY, minZ), (minX, renderMaxY, minZ));
            if ((cell.ExposedFaces & TerrainLodFaceMask.South) != 0 &&
                z < level.Depth - 1 && ShouldEmitLiquidSide(x, y, z + 1) &&
                !CullUndergroundFace(Side.South))
                AddFace(Side.South, 0.8f, tileX, tileY,
                    (minX, renderMaxY, maxZ), (minX, minY, maxZ),
                    (maxX, minY, maxZ), (maxX, renderMaxY, maxZ));

            bool ShouldEmitLiquidSide(int neighborX, int neighborY, int neighborZ)
            {
                if (!translucent || material.Geometry != TerrainLodGeometryClass.Liquid)
                    return true;
                var neighborCell = level[neighborX, neighborY, neighborZ];
                return !TrySelectTranslucentMaterial(neighborCell, out var neighborMaterial) ||
                       !SharesLiquidMedium(material, neighborMaterial, blocks);
            }

            bool CullUndergroundFace(Side side)
            {
                if (caveCullBelowY is not { } ceilingY || !hasSkyLight || lighting is null)
                    return false;
                var worldTop = maxY + VerticalOrigin;
                if (worldTop > ceilingY) return false;
                return SampleFaceLight(side, minimumBlockLight: 0).Sky == 0;
            }

            void AddFace(
                Side side,
                float shade,
                float tileU,
                float tileV,
                (float X, float Y, float Z) a,
                (float X, float Y, float Z) b,
                (float X, float Y, float Z) c,
                (float X, float Y, float Z) d)
            {
                var light = SampleFaceLight(side, block.LightEmission);
                var sampleX = (int)MathF.Floor(
                    hierarchy.ChunkX * 16 + (minX + maxX) * 0.5f);
                var sampleY = (int)MathF.Floor((minY + maxY) * 0.5f + VerticalOrigin);
                var sampleZ = (int)MathF.Floor(
                    hierarchy.ChunkZ * 16 + (minZ + maxZ) * 0.5f);
                var tint = visuals is null
                    ? block.GetColorForFace(material.Metadata, (int)side)
                    : block.GetColorMultiplier(
                        visuals, sampleX, sampleY, sampleZ, material.Metadata);
                var appearance = ResolveFaceAppearance(
                    block, material.Metadata, side, tint,
                    ReferenceEquals(block, grassBlock), grassOverlayTexture);
                if (levelIndex > 0 && side == Side.Up &&
                    TryFindSurfaceSample(out var sample, out var sampleCoverage))
                    appearance = ApplySurfaceSample(
                        appearance, sample, sampleCoverage,
                        CoverageOf(cell, material), sampleX, sampleY, sampleZ);

                EmitQuad(appearance.Texture, appearance.Tint);
                if (appearance.OverlayTexture >= 0)
                    EmitQuad(appearance.OverlayTexture, appearance.OverlayTint);

                void EmitQuad(int texture, int faceTint)
                {
                    var layer = Atlases.Terrain.LayerOfGridIndex(texture);
                    var color = PackTintedColor(faceTint, shade);
                    vertices.Add(ChunkVertexHelper.Create(color, a.X, a.Y, a.Z, tileU, 0, layer));
                    vertices.Add(ChunkVertexHelper.Create(color, b.X, b.Y, b.Z, tileU, tileV, layer));
                    vertices.Add(ChunkVertexHelper.Create(color, c.X, c.Y, c.Z, 0, tileV, layer));
                    vertices.Add(ChunkVertexHelper.Create(color, d.X, d.Y, d.Z, 0, 0, layer));
                    lights.Add(light);
                    lights.Add(light);
                    lights.Add(light);
                    lights.Add(light);
                }
            }

            void EmitCrossedQuad()
            {
                var texture = block.GetTexture(Side.Down, material.Metadata);
                var layerIndex = Atlases.Terrain.LayerOfGridIndex(texture);
                var sampleX = hierarchy.ChunkX * 16 + (minX + maxX) * 0.5f;
                var sampleY = (int)MathF.Floor((minY + maxY) * 0.5f + VerticalOrigin);
                var sampleZ = hierarchy.ChunkZ * 16 + (minZ + maxZ) * 0.5f;
                var tint = visuals is null
                    ? block.GetColorForFace(material.Metadata, (int)Side.Up)
                    : block.GetColorMultiplier(
                        visuals, (int)sampleX, sampleY, (int)sampleZ, material.Metadata);
                var color = PackTintedColor(tint, 1);
                var light = SampleFaceLight(Side.Up, block.LightEmission);
                const float inset = 0.05f;
                var left = minX + inset;
                var right = maxX - inset;
                var north = minZ + inset;
                var south = maxZ - inset;

                EmitTwoSidedPlane(
                    (left, maxY, north), (left, minY, north),
                    (right, minY, south), (right, maxY, south));
                EmitTwoSidedPlane(
                    (left, maxY, south), (left, minY, south),
                    (right, minY, north), (right, maxY, north));

                void EmitTwoSidedPlane(
                    (float X, float Y, float Z) a,
                    (float X, float Y, float Z) b,
                    (float X, float Y, float Z) c,
                    (float X, float Y, float Z) d)
                {
                    Emit(a, b, c, d);
                    Emit(d, c, b, a);
                }

                void Emit(
                    (float X, float Y, float Z) a,
                    (float X, float Y, float Z) b,
                    (float X, float Y, float Z) c,
                    (float X, float Y, float Z) d)
                {
                    vertices.Add(ChunkVertexHelper.Create(
                        color, a.X, a.Y, a.Z, 0, 0, layerIndex));
                    vertices.Add(ChunkVertexHelper.Create(
                        color, b.X, b.Y, b.Z, 0, 1, layerIndex));
                    vertices.Add(ChunkVertexHelper.Create(
                        color, c.X, c.Y, c.Z, 1, 1, layerIndex));
                    vertices.Add(ChunkVertexHelper.Create(
                        color, d.X, d.Y, d.Z, 1, 0, layerIndex));
                    for (var index = 0; index < 4; index++) lights.Add(light);
                }
            }

            bool TryFindSurfaceSample(
                out TerrainLodMaterial sample,
                out uint coverage)
            {
                if (TrySelectDetailSample(cell, out sample, out coverage)) return true;
                if (y + 1 < level.Height &&
                    TrySelectDetailSample(level[x, y + 1, z], out sample, out coverage))
                    return true;
                sample = default;
                coverage = 0;
                return false;
            }

            TerrainLodFaceAppearance ApplySurfaceSample(
                TerrainLodFaceAppearance baseAppearance,
                TerrainLodMaterial sample,
                uint sampleCoverage,
                uint baseCoverage,
                float sampleX,
                int sampleY,
                float sampleZ)
            {
                if (sample.Geometry == TerrainLodGeometryClass.SurfaceLayer &&
                    blocks.TryGet(sample.BlockId, out var sampleBlock) && sampleBlock is not null)
                {
                    var sampleTint = visuals is null
                        ? sampleBlock.GetColorForFace(sample.Metadata, (int)Side.Up)
                        : sampleBlock.GetColorMultiplier(
                            visuals, (int)sampleX, sampleY + 1, (int)sampleZ, sample.Metadata);
                    return ResolveFaceAppearance(
                        sampleBlock, sample.Metadata, Side.Up, sampleTint,
                        ReferenceEquals(sampleBlock, grassBlock), grassOverlayTexture);
                }

                var totalCoverage = Math.Max(1u, sampleCoverage + baseCoverage);
                var weight = Math.Clamp(sampleCoverage / (float)totalCoverage, 0.125f, 0.35f);
                return baseAppearance with
                {
                    Tint = BlendTint(baseAppearance.Tint, (int)sample.MapColor, weight),
                    OverlayTint = BlendTint(
                        baseAppearance.OverlayTint, (int)sample.MapColor, weight)
                };
            }

            ChunkLightVertex SampleFaceLight(Side side, int minimumBlockLight)
            {
                if (lighting is null) return fallbackLight;
                var sampleX = hierarchy.ChunkX * 16 + (minX + maxX) * 0.5f;
                var sampleY = (minY + maxY) * 0.5f + VerticalOrigin;
                var sampleZ = hierarchy.ChunkZ * 16 + (minZ + maxZ) * 0.5f;
                switch (side)
                {
                    case Side.Down: sampleY = minY + VerticalOrigin - 1; break;
                    case Side.Up: sampleY = maxY + VerticalOrigin; break;
                    case Side.North: sampleZ = hierarchy.ChunkZ * 16 + minZ - 1; break;
                    case Side.South: sampleZ = hierarchy.ChunkZ * 16 + maxZ; break;
                    case Side.West: sampleX = hierarchy.ChunkX * 16 + minX - 1; break;
                    case Side.East: sampleX = hierarchy.ChunkX * 16 + maxX; break;
                }

                var levels = lighting.GetLightLevels(
                    (int)MathF.Floor(sampleX),
                    (int)MathF.Floor(sampleY),
                    (int)MathF.Floor(sampleZ),
                    minimumBlockLight);
                return new ChunkLightVertex(
                    ChunkVertexHelper.ToQuarterLevels(levels.Sky),
                    ChunkVertexHelper.ToQuarterLevels(levels.Block));
            }
        }

        if (!translucent) return new TerrainLodLayerData([.. vertices], [.. lights]);
        var merged = TerrainLodHorizontalQuadMerger.Merge([.. vertices], [.. lights]);
        return new TerrainLodLayerData(merged.Vertices, merged.Lights);
    }

    internal static bool IsDepthWriting(TerrainLodMaterial material) =>
        material.Geometry is TerrainLodGeometryClass.Opaque or
            TerrainLodGeometryClass.Cutout or
            TerrainLodGeometryClass.ConservativeCube or
            TerrainLodGeometryClass.BoundedCube or
            TerrainLodGeometryClass.CrossedQuad or
            TerrainLodGeometryClass.SurfaceLayer;

    internal static bool IsVolumetricDepthWriting(TerrainLodMaterial material) =>
        material.Geometry is TerrainLodGeometryClass.Opaque or
            TerrainLodGeometryClass.Cutout or
            TerrainLodGeometryClass.ConservativeCube or
            TerrainLodGeometryClass.BoundedCube;

    internal static bool IsTranslucent(TerrainLodMaterial material) =>
        material.Geometry is TerrainLodGeometryClass.Liquid or
            TerrainLodGeometryClass.Translucent;

    /// <summary>
    ///     Fluid metadata and stationary/flowing block variants describe the surface, not a
    ///     boundary between two media. The exact renderer makes this decision from the canonical
    ///     block material; LOD construction must use the same rule or it exposes a cube grid inside
    ///     continuous water and lava volumes.
    /// </summary>
    internal static bool SharesLiquidMedium(
        TerrainLodMaterial first,
        TerrainLodMaterial second,
        IBlockRuntimeView blocks)
    {
        if (first.Geometry != TerrainLodGeometryClass.Liquid ||
            second.Geometry != TerrainLodGeometryClass.Liquid)
            return false;
        if (first.BlockId == second.BlockId) return true;
        return blocks.TryGet(first.BlockId, out var firstBlock) && firstBlock is not null &&
               blocks.TryGet(second.BlockId, out var secondBlock) && secondBlock is not null &&
               ReferenceEquals(firstBlock.Material, secondBlock.Material);
    }

    internal static bool TrySelectDepthMaterial(
        in TerrainLodCell cell,
        out TerrainLodMaterial material)
    {
        // Keep the reducer's visual choice when it can use the depth pass. If a thin liquid or
        // glass surface won the surface-preserving score, recover the strongest retained solid
        // material behind it instead of cutting a hole in the terrain until that later pass exists.
        if (IsDepthWriting(cell.Primary))
        {
            material = cell.Primary;
            return true;
        }
        if (cell.HasSecondary && IsDepthWriting(cell.Secondary))
        {
            material = cell.Secondary;
            return true;
        }
        if (cell.HasTertiary && IsDepthWriting(cell.Tertiary))
        {
            material = cell.Tertiary;
            return true;
        }
        material = default;
        return false;
    }

    internal static bool TrySelectVolumetricDepthMaterial(
        in TerrainLodCell cell,
        out TerrainLodMaterial material)
    {
        if (cell.PrimaryCoverage != 0 && IsVolumetricDepthWriting(cell.Primary))
        {
            material = cell.Primary;
            return true;
        }
        if (cell.SecondaryCoverage != 0 && IsVolumetricDepthWriting(cell.Secondary))
        {
            material = cell.Secondary;
            return true;
        }
        if (cell.TertiaryCoverage != 0 && IsVolumetricDepthWriting(cell.Tertiary))
        {
            material = cell.Tertiary;
            return true;
        }
        material = default;
        return false;
    }

    internal static bool TrySelectDetailSample(
        in TerrainLodCell cell,
        out TerrainLodMaterial material,
        out uint coverage)
    {
        // Prefer a continuous surface such as snow over sparse crossed foliage if both survive
        // reduction. Either remains a sample at parent levels, never an inflated solid cell.
        if (TrySelectDetailGeometry(
                cell, TerrainLodGeometryClass.SurfaceLayer, out material, out coverage) ||
            TrySelectDetailGeometry(
                cell, TerrainLodGeometryClass.CrossedQuad, out material, out coverage)) return true;
        material = default;
        coverage = 0;
        return false;
    }

    private static bool TrySelectDetailGeometry(
        in TerrainLodCell cell,
        TerrainLodGeometryClass geometry,
        out TerrainLodMaterial material,
        out uint coverage)
    {
        if (cell.PrimaryCoverage != 0 && cell.Primary.Geometry == geometry)
        {
            material = cell.Primary;
            coverage = cell.PrimaryCoverage;
            return true;
        }
        if (cell.SecondaryCoverage != 0 && cell.Secondary.Geometry == geometry)
        {
            material = cell.Secondary;
            coverage = cell.SecondaryCoverage;
            return true;
        }
        if (cell.TertiaryCoverage != 0 && cell.Tertiary.Geometry == geometry)
        {
            material = cell.Tertiary;
            coverage = cell.TertiaryCoverage;
            return true;
        }
        material = default;
        coverage = 0;
        return false;
    }
    internal static bool TrySelectTranslucentMaterial(
        in TerrainLodCell cell,
        out TerrainLodMaterial material)
    {
        if (IsTranslucent(cell.Primary))
        {
            material = cell.Primary;
            return true;
        }
        if (cell.HasSecondary && IsTranslucent(cell.Secondary))
        {
            material = cell.Secondary;
            return true;
        }
        if (cell.HasTertiary && IsTranslucent(cell.Tertiary))
        {
            material = cell.Tertiary;
            return true;
        }
        material = default;
        return false;
    }

    internal static TerrainLodFaceAppearance ResolveFaceAppearance(
        Block block,
        int metadata,
        Side side,
        int tint,
        bool isGrassBlock,
        int grassOverlayTexture)
    {
        var texture = block.GetTexture(side, metadata);
        if (!isGrassBlock) return new TerrainLodFaceAppearance(texture, tint);
        if (side == Side.Up) return new TerrainLodFaceAppearance(texture, tint);
        if (side == Side.Down) return new TerrainLodFaceAppearance(texture, 0xFFFFFF);
        return new TerrainLodFaceAppearance(texture, 0xFFFFFF, grassOverlayTexture, tint);
    }

    internal static int PackTintedColor(int tint, float shade)
    {
        var red = Shade((tint >> 16) & 255);
        var green = Shade((tint >> 8) & 255);
        var blue = Shade(tint & 255);
        return PackColor(red, green, blue, byte.MaxValue);

        byte Shade(int channel) =>
            (byte)Math.Clamp((int)MathF.Round(channel * shade), 0, byte.MaxValue);
    }

    private static int PackColor(byte red, byte green, byte blue, byte alpha) =>
        BitConverter.IsLittleEndian
            ? alpha << 24 | blue << 16 | green << 8 | red
            : red << 24 | green << 16 | blue << 8 | alpha;

    internal static int BlendTint(int first, int second, float weight)
    {
        weight = Math.Clamp(weight, 0, 1);
        byte Blend(int shift) => (byte)Math.Clamp(
            (int)MathF.Round(((first >> shift) & 255) * (1 - weight) +
                             ((second >> shift) & 255) * weight),
            0, 255);
        return Blend(16) << 16 | Blend(8) << 8 | Blend(0);
    }

    private static uint CoverageOf(in TerrainLodCell cell, TerrainLodMaterial material)
    {
        if (cell.Primary == material) return cell.PrimaryCoverage;
        if (cell.Secondary == material) return cell.SecondaryCoverage;
        if (cell.Tertiary == material) return cell.TertiaryCoverage;
        return 0;
    }

    private readonly record struct TerrainLodLayerData(
        ChunkVertex[] Vertices,
        ChunkLightVertex[] Lights);
}
