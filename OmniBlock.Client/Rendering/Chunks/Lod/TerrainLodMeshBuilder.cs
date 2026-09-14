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
            var solid = TerrainLodMeshBuilder.TrySelectDepthMaterial(cell, out var depth)
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

internal readonly record struct TerrainLodNeighborBoundaries(
    TerrainLodBoundarySummary? North,
    TerrainLodBoundarySummary? South,
    TerrainLodBoundarySummary? West,
    TerrainLodBoundarySummary? East)
{
    public bool TryGet(
        int level,
        Side side,
        int along,
        int y,
        bool translucent,
        out TerrainLodMaterial material)
    {
        var (neighbor, neighborSide) = side switch
        {
            Side.North => (North, Side.South),
            Side.South => (South, Side.North),
            Side.West => (West, Side.East),
            Side.East => (East, Side.West),
            _ => (null, side)
        };
        if (neighbor is null)
        {
            material = TerrainLodMaterial.Air;
            return false;
        }
        return neighbor.TryGet(level, neighborSide, along, y, translucent, out material);
    }
}

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
        TerrainLodNeighborBoundaries neighbors = default)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        ArgumentNullException.ThrowIfNull(blocks);
        if ((uint)levelIndex >= (uint)hierarchy.Levels.Count)
            throw new ArgumentOutOfRangeException(nameof(levelIndex));

        var solid = BuildLayer(
            hierarchy, levelIndex, blocks, hasSkyLight, lighting, visuals, neighbors, false);
        var translucent = BuildLayer(
            hierarchy, levelIndex, blocks, hasSkyLight, lighting, visuals, neighbors, true);
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
        TerrainLodNeighborBoundaries neighbors,
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
            if (cell.IsEmpty || !(translucent
                    ? TrySelectTranslucentMaterial(cell, out material)
                    : TrySelectDepthMaterial(cell, out material))) continue;
            if (!blocks.TryGet(material.BlockId, out var block) || block is null) continue;

            var minX = x * level.Scale;
            var minY = y * level.Scale - VerticalOrigin;
            var minZ = z * level.Scale;
            var maxX = Math.Min((x + 1) * level.Scale, 16);
            var maxY = Math.Min((y + 1) * level.Scale, ChuckFormat.WorldHeight) - VerticalOrigin;
            var maxZ = Math.Min((z + 1) * level.Scale, 16);
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

            if ((cell.ExposedFaces & TerrainLodFaceMask.Down) != 0)
                AddFace(Side.Down, 0.5f, tileX, tileZ,
                    (minX, minY, maxZ), (minX, minY, minZ),
                    (maxX, minY, minZ), (maxX, minY, maxZ));
            if ((cell.ExposedFaces & TerrainLodFaceMask.Up) != 0)
                AddFace(Side.Up, 1.0f, tileX, tileZ,
                    (maxX, renderMaxY, maxZ), (maxX, renderMaxY, minZ),
                    (minX, renderMaxY, minZ), (minX, renderMaxY, maxZ));
            if ((cell.ExposedFaces & TerrainLodFaceMask.West) != 0 &&
                (x > 0 || BoundaryExposed(Side.West, z, y, material)))
                AddFace(Side.West, 0.6f, tileZ, tileY,
                    (minX, renderMaxY, minZ), (minX, minY, minZ),
                    (minX, minY, maxZ), (minX, renderMaxY, maxZ));
            if ((cell.ExposedFaces & TerrainLodFaceMask.East) != 0 &&
                (x < level.Width - 1 || BoundaryExposed(Side.East, z, y, material)))
                AddFace(Side.East, 0.6f, tileZ, tileY,
                    (maxX, renderMaxY, maxZ), (maxX, minY, maxZ),
                    (maxX, minY, minZ), (maxX, renderMaxY, minZ));
            if ((cell.ExposedFaces & TerrainLodFaceMask.North) != 0 &&
                (z > 0 || BoundaryExposed(Side.North, x, y, material)))
                AddFace(Side.North, 0.8f, tileX, tileY,
                    (maxX, renderMaxY, minZ), (maxX, minY, minZ),
                    (minX, minY, minZ), (minX, renderMaxY, minZ));
            if ((cell.ExposedFaces & TerrainLodFaceMask.South) != 0 &&
                (z < level.Depth - 1 || BoundaryExposed(Side.South, x, y, material)))
                AddFace(Side.South, 0.8f, tileX, tileY,
                    (minX, renderMaxY, maxZ), (minX, minY, maxZ),
                    (maxX, minY, maxZ), (maxX, renderMaxY, maxZ));

            bool BoundaryExposed(Side side, int along, int cellY, TerrainLodMaterial current)
            {
                if (!neighbors.TryGet(levelIndex, side, along, cellY, translucent,
                        out var neighbor)) return false;
                return neighbor.IsAir || !neighbor.OccludesFaces ||
                       current.Geometry == TerrainLodGeometryClass.Liquid && neighbor != current;
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
                var sampleX = hierarchy.ChunkX * 16 + (minX + maxX) / 2;
                var sampleY = (int)MathF.Floor((minY + maxY) * 0.5f + VerticalOrigin);
                var sampleZ = hierarchy.ChunkZ * 16 + (minZ + maxZ) / 2;
                var tint = visuals is null
                    ? block.GetColorForFace(material.Metadata, (int)side)
                    : block.GetColorMultiplier(
                        visuals, sampleX, sampleY, sampleZ, material.Metadata);
                var appearance = ResolveFaceAppearance(
                    block, material.Metadata, side, tint,
                    ReferenceEquals(block, grassBlock), grassOverlayTexture);

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

        return new TerrainLodLayerData([.. vertices], [.. lights]);
    }

    internal static bool IsDepthWriting(TerrainLodMaterial material) =>
        material.Geometry is TerrainLodGeometryClass.Opaque or
            TerrainLodGeometryClass.Cutout or
            TerrainLodGeometryClass.ConservativeCube;

    internal static bool IsTranslucent(TerrainLodMaterial material) =>
        material.Geometry is TerrainLodGeometryClass.Liquid or
            TerrainLodGeometryClass.Translucent;

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

    private readonly record struct TerrainLodLayerData(
        ChunkVertex[] Vertices,
        ChunkLightVertex[] Lights);
}
