using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using OmniBlock.Blocks;
using OmniBlock.NBT;
using OmniBlock.Registries;
using OmniBlock.Worlds.Chunks;

namespace OmniBlock.Worlds.Lod;

public enum TerrainLodGeometryClass : byte
{
    Air,
    Opaque,
    Cutout,
    Liquid,
    Translucent,
    ConservativeCube
}

public enum TerrainLodReductionStrategy : byte
{
    VolumeDominant,
    SurfacePreserving
}

[Flags]
public enum TerrainLodFaceMask : byte
{
    None = 0,
    Down = 1 << 0,
    Up = 1 << 1,
    North = 1 << 2,
    South = 1 << 3,
    West = 1 << 4,
    East = 1 << 5,
    All = Down | Up | North | South | West | East
}

/// <summary>
///     Resource-pack-independent material identity retained by the terrain LOD hierarchy.
///     Texture selection and GPU resources deliberately remain client concerns.
/// </summary>
public readonly record struct TerrainLodMaterial(
    ResourceLocation BlockId,
    byte Metadata,
    TerrainLodGeometryClass Geometry,
    bool OccludesFaces,
    uint MapColor)
{
    public static TerrainLodMaterial Air { get; } = new(
        new ResourceLocation(Namespace.OmniBlock, "air"),
        0,
        TerrainLodGeometryClass.Air,
        false,
        0);

    public bool IsAir => Geometry == TerrainLodGeometryClass.Air;
}

public readonly record struct TerrainLodMaterialDefinition(
    int ProtocolId,
    ResourceLocation BlockId,
    TerrainLodGeometryClass Geometry,
    bool OccludesFaces,
    uint MapColor);

/// <summary>Resolves transport IDs into stable catalog identities before reduction.</summary>
public sealed class TerrainLodMaterialCatalog
{
    private readonly FrozenDictionary<int, TerrainLodMaterialDefinition> _byProtocolId;

    public TerrainLodMaterialCatalog(IEnumerable<TerrainLodMaterialDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var byProtocolId = new Dictionary<int, TerrainLodMaterialDefinition>();
        foreach (var definition in definitions)
        {
            if (definition.ProtocolId is <= 0 or > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(definitions), definition.ProtocolId,
                    "Terrain LOD block protocol IDs must be between 1 and 255; zero is air.");
            if (!byProtocolId.TryAdd(definition.ProtocolId, definition))
                throw new ArgumentException(
                    $"Duplicate terrain LOD protocol ID {definition.ProtocolId}.",
                    nameof(definitions));
        }
        _byProtocolId = byProtocolId.ToFrozenDictionary();
        RulesFingerprint = ComputeRulesFingerprint(_byProtocolId.Values);
    }

    public string RulesFingerprint { get; }

    public static TerrainLodMaterialCatalog FromRuntime(ContentRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return new TerrainLodMaterialCatalog(runtime.Manifest.BlockIds
            .Where(static entry => entry.Value != 0)
            .OrderBy(static entry => entry.Key)
            .Select(entry =>
            {
                var block = runtime.Blocks.Get(entry.Key);
                var geometry = Classify(block);
                return new TerrainLodMaterialDefinition(
                    block.Id,
                    entry.Key,
                    geometry,
                    geometry is TerrainLodGeometryClass.Opaque or
                        TerrainLodGeometryClass.ConservativeCube,
                    block.Material.MapColor.ColorValue);
            }));
    }

    public TerrainLodMaterial Resolve(int protocolId, int metadata)
    {
        if (protocolId == 0) return TerrainLodMaterial.Air;
        if (!_byProtocolId.TryGetValue(protocolId, out var definition))
            throw new InvalidDataException(
                $"Terrain snapshot references unknown block protocol ID {protocolId}.");
        if (metadata is < 0 or > 15)
            throw new InvalidDataException($"Terrain snapshot metadata {metadata} is outside 0..15.");
        return new TerrainLodMaterial(
            definition.BlockId,
            (byte)metadata,
            definition.Geometry,
            definition.OccludesFaces,
            definition.MapColor);
    }

    private static TerrainLodGeometryClass Classify(Block block)
    {
        if (block.Material.IsFluid || block.RenderType == BlockRendererType.Fluids)
            return TerrainLodGeometryClass.Liquid;
        if (block.RenderType == BlockRendererType.Entity)
            return TerrainLodGeometryClass.ConservativeCube;
        // Transparent burnable material describes porous vegetation rather than a continuous
        // refractive surface. This fallback is independent of the fancy-leaves runtime toggle.
        if (block.Material.IsTransparent && block.Material.IsBurnable)
            return TerrainLodGeometryClass.Cutout;
        if (block.IsOpaque) return TerrainLodGeometryClass.Opaque;
        if (block.RenderLayer > 0 || block.Material.IsTransparent)
            return TerrainLodGeometryClass.Translucent;
        return TerrainLodGeometryClass.Cutout;
    }

    private static string ComputeRulesFingerprint(
        IEnumerable<TerrainLodMaterialDefinition> definitions)
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var definition in definitions.OrderBy(static value => value.ProtocolId))
            {
                writer.Write(definition.ProtocolId);
                writer.Write(definition.BlockId.ToString());
                writer.Write((byte)definition.Geometry);
                writer.Write(definition.OccludesFaces);
                writer.Write(definition.MapColor);
            }
        }
        return Convert.ToHexStringLower(
            SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length)));
    }
}

/// <summary>Immutable block-and-metadata snapshot with no entities or world activation.</summary>
public sealed class TerrainLodSourceSnapshot
{
    private readonly byte[] _blocks;
    private readonly byte[] _metadata;

    public TerrainLodSourceSnapshot(
        int chunkX,
        int chunkZ,
        int width,
        int height,
        int depth,
        ReadOnlySpan<byte> blocks,
        ReadOnlySpan<byte> metadata,
        long terrainRevision = 0)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (depth <= 0) throw new ArgumentOutOfRangeException(nameof(depth));
        var length = checked(width * height * depth);
        if (blocks.Length != length || metadata.Length != length)
            throw new ArgumentException(
                $"Terrain snapshot dimensions require {length} block and metadata entries.");
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        Width = width;
        Height = height;
        Depth = depth;
        TerrainRevision = terrainRevision;
        _blocks = blocks.ToArray();
        _metadata = metadata.ToArray();
    }

    public int ChunkX { get; }
    public int ChunkZ { get; }
    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    public long TerrainRevision { get; }
    public long EstimatedBytes => (long)_blocks.Length + _metadata.Length;

    public static TerrainLodSourceSnapshot Capture(Chunk chunk, long? terrainRevision = null)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var metadata = new byte[ChuckFormat.ChunkSize];
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = 0; y < ChuckFormat.ChunkHeight; y++)
            metadata[ChuckFormat.GetIndex(x, y, z)] = (byte)chunk.Meta.GetNibble(x, y, z);
        return new TerrainLodSourceSnapshot(
            chunk.X,
            chunk.Z,
            16,
            ChuckFormat.ChunkHeight,
            16,
            chunk.Blocks,
            metadata,
            terrainRevision ?? chunk.TerrainRevision);
    }

    /// <summary>
    ///     Reads only terrain arrays from a region-format Level compound. Entities, block
    ///     entities, ticks, and world callbacks are deliberately never materialized.
    /// </summary>
    public static TerrainLodSourceSnapshot FromRegionNbt(
        NBTTagCompound level,
        long? terrainRevision = null)
    {
        ArgumentNullException.ThrowIfNull(level);
        var blocks = level.GetByteArray("Blocks");
        var packedMetadata = level.GetByteArray("Data");
        if (blocks.Length != ChuckFormat.ChunkSize)
            throw new InvalidDataException(
                $"Chunk terrain contains {blocks.Length} block bytes; expected {ChuckFormat.ChunkSize}.");
        if (packedMetadata.Length != ChuckFormat.ChunkSize / 2)
            throw new InvalidDataException(
                $"Chunk terrain contains {packedMetadata.Length} metadata bytes; expected " +
                $"{ChuckFormat.ChunkSize / 2}.");

        var metadata = new byte[ChuckFormat.ChunkSize];
        var nibbles = new ChunkNibbleArray(packedMetadata);
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = 0; y < ChuckFormat.ChunkHeight; y++)
            metadata[ChuckFormat.GetIndex(x, y, z)] = (byte)nibbles.GetNibble(x, y, z);
        return new TerrainLodSourceSnapshot(
            level.GetInteger("xPos"),
            level.GetInteger("zPos"),
            16,
            ChuckFormat.ChunkHeight,
            16,
            blocks,
            metadata,
            terrainRevision ?? (level.HasKey("TerrainRevision")
                ? level.GetLong("TerrainRevision")
                : 0));
    }

    public byte GetBlock(int x, int y, int z) => _blocks[Index(x, y, z)];
    public byte GetMetadata(int x, int y, int z) => _metadata[Index(x, y, z)];

    private int Index(int x, int y, int z)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height || (uint)z >= (uint)Depth)
            throw new ArgumentOutOfRangeException(nameof(x), $"Cell {x},{y},{z} is outside the snapshot.");
        return (x * Depth + z) * Height + y;
    }
}

/// <summary>
///     One reduced voxel. OccupancyMask records which immediate 2x2x2 child octants contain
///     terrain; coverage remains measured in original source voxels.
/// </summary>
public readonly record struct TerrainLodCell(
    TerrainLodMaterial Primary,
    TerrainLodMaterial Secondary,
    TerrainLodMaterial Tertiary,
    uint PrimaryCoverage,
    uint SecondaryCoverage,
    uint TertiaryCoverage,
    byte OccupancyMask,
    TerrainLodFaceMask ExposedFaces)
{
    public bool IsEmpty => PrimaryCoverage == 0;
    public bool HasSecondary => SecondaryCoverage != 0;
    public bool HasTertiary => TertiaryCoverage != 0;
}

public sealed class TerrainLodLevel
{
    private readonly TerrainLodCell[] _cells;

    internal TerrainLodLevel(int level, int width, int height, int depth, TerrainLodCell[] cells)
    {
        Level = level;
        Scale = 1 << level;
        Width = width;
        Height = height;
        Depth = depth;
        _cells = cells;
    }

    public int Level { get; }
    public int Scale { get; }
    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
    public int CellCount => _cells.Length;

    public TerrainLodCell this[int x, int y, int z] => _cells[Index(x, y, z)];
    internal TerrainLodCell[] MutableCells => _cells;

    internal int Index(int x, int y, int z) => (x * Depth + z) * Height + y;
}

public sealed class TerrainLodHierarchy
{
    internal TerrainLodHierarchy(
        int chunkX,
        int chunkZ,
        long terrainRevision,
        TerrainLodReductionStrategy strategy,
        TerrainLodLevel[] levels)
    {
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        TerrainRevision = terrainRevision;
        Strategy = strategy;
        Levels = Array.AsReadOnly(levels);
        CanonicalHash = ComputeCanonicalHash();
    }

    public const int ReductionSchemaVersion = 1;
    public int ChunkX { get; }
    public int ChunkZ { get; }
    public long TerrainRevision { get; }
    public TerrainLodReductionStrategy Strategy { get; }
    public IReadOnlyList<TerrainLodLevel> Levels { get; }
    public string CanonicalHash { get; }

    private string ComputeCanonicalHash()
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(ReductionSchemaVersion);
            writer.Write(ChunkX);
            writer.Write(ChunkZ);
            writer.Write(TerrainRevision);
            writer.Write((byte)Strategy);
            writer.Write(Levels.Count);
            foreach (var level in Levels)
            {
                writer.Write(level.Width);
                writer.Write(level.Height);
                writer.Write(level.Depth);
                foreach (var cell in level.MutableCells)
                {
                    WriteMaterial(writer, cell.Primary);
                    WriteMaterial(writer, cell.Secondary);
                    WriteMaterial(writer, cell.Tertiary);
                    writer.Write(cell.PrimaryCoverage);
                    writer.Write(cell.SecondaryCoverage);
                    writer.Write(cell.TertiaryCoverage);
                    writer.Write(cell.OccupancyMask);
                    writer.Write((byte)cell.ExposedFaces);
                }
            }
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length)));
    }

    private static void WriteMaterial(BinaryWriter writer, TerrainLodMaterial material)
    {
        writer.Write(material.BlockId?.ToString() ?? TerrainLodMaterial.Air.BlockId.ToString());
        writer.Write(material.Metadata);
        writer.Write((byte)material.Geometry);
        writer.Write(material.OccludesFaces);
        writer.Write(material.MapColor);
    }
}

public static class TerrainLodReducer
{
    public static TerrainLodHierarchy Build(
        TerrainLodSourceSnapshot source,
        TerrainLodMaterialCatalog materials,
        TerrainLodReductionStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(materials);
        var levels = new List<TerrainLodLevel>();
        var leafCells = new TerrainLodCell[checked(source.Width * source.Height * source.Depth)];
        var leaf = new TerrainLodLevel(0, source.Width, source.Height, source.Depth, leafCells);
        for (var x = 0; x < source.Width; x++)
        for (var z = 0; z < source.Depth; z++)
        for (var y = 0; y < source.Height; y++)
        {
            var material = materials.Resolve(source.GetBlock(x, y, z), source.GetMetadata(x, y, z));
            leafCells[leaf.Index(x, y, z)] = material.IsAir
                ? default
                : new TerrainLodCell(
                    material, default, default, 1, 0, 0,
                    byte.MaxValue, TerrainLodFaceMask.None);
        }
        ComputeExposedFaces(leaf);
        levels.Add(leaf);

        while (levels[^1].Width > 1 || levels[^1].Height > 1 || levels[^1].Depth > 1)
        {
            var child = levels[^1];
            var width = (child.Width + 1) / 2;
            var height = (child.Height + 1) / 2;
            var depth = (child.Depth + 1) / 2;
            var cells = new TerrainLodCell[checked(width * height * depth)];
            var parent = new TerrainLodLevel(child.Level + 1, width, height, depth, cells);
            for (var x = 0; x < width; x++)
            for (var z = 0; z < depth; z++)
            for (var y = 0; y < height; y++)
                cells[parent.Index(x, y, z)] = ReduceCell(child, x, y, z, strategy);
            ComputeExposedFaces(parent);
            levels.Add(parent);
        }

        return new TerrainLodHierarchy(
            source.ChunkX,
            source.ChunkZ,
            source.TerrainRevision,
            strategy,
            [.. levels]);
    }

    private static TerrainLodCell ReduceCell(
        TerrainLodLevel child,
        int parentX,
        int parentY,
        int parentZ,
        TerrainLodReductionStrategy strategy)
    {
        Dictionary<TerrainLodMaterial, Candidate> candidates = [];
        byte occupancy = 0;
        for (var dx = 0; dx < 2; dx++)
        for (var dz = 0; dz < 2; dz++)
        for (var dy = 0; dy < 2; dy++)
        {
            var x = parentX * 2 + dx;
            var y = parentY * 2 + dy;
            var z = parentZ * 2 + dz;
            if (x >= child.Width || y >= child.Height || z >= child.Depth) continue;
            var cell = child[x, y, z];
            if (cell.IsEmpty) continue;
            var bit = dx | (dy << 1) | (dz << 2);
            occupancy |= (byte)(1 << bit);
            Add(cell.Primary, cell.PrimaryCoverage, cell.ExposedFaces);
            if (cell.HasSecondary)
                Add(cell.Secondary, cell.SecondaryCoverage, cell.ExposedFaces);
            if (cell.HasTertiary)
                Add(cell.Tertiary, cell.TertiaryCoverage, cell.ExposedFaces);
        }

        if (candidates.Count == 0) return default;
        var selected = candidates
            .OrderByDescending(static pair => pair.Value.Score)
            .ThenByDescending(static pair => pair.Value.Coverage)
            .ThenBy(static pair => pair.Key.BlockId)
            .ThenBy(static pair => pair.Key.Metadata)
            .Take(3)
            .ToArray();
        var primary = selected[0];
        var secondary = selected.Length > 1 ? selected[1] : default;
        var tertiary = selected.Length > 2 ? selected[2] : default;
        return new TerrainLodCell(
            primary.Key,
            secondary.Key,
            tertiary.Key,
            primary.Value.Coverage,
            secondary.Value.Coverage,
            tertiary.Value.Coverage,
            occupancy,
            TerrainLodFaceMask.None);

        void Add(TerrainLodMaterial material, uint coverage, TerrainLodFaceMask exposed)
        {
            if (coverage == 0 || material.IsAir) return;
            var visualBonus = strategy == TerrainLodReductionStrategy.SurfacePreserving &&
                              exposed != TerrainLodFaceMask.None &&
                              material.Geometry is TerrainLodGeometryClass.Cutout or
                                  TerrainLodGeometryClass.Liquid or
                                  TerrainLodGeometryClass.Translucent
                ? checked(coverage * 4)
                : 0;
            var previous = candidates.GetValueOrDefault(material);
            candidates[material] = new Candidate(
                checked(previous.Coverage + coverage),
                checked(previous.Score + coverage + visualBonus));
        }
    }

    private static void ComputeExposedFaces(TerrainLodLevel level)
    {
        for (var x = 0; x < level.Width; x++)
        for (var z = 0; z < level.Depth; z++)
        for (var y = 0; y < level.Height; y++)
        {
            var index = level.Index(x, y, z);
            var cell = level.MutableCells[index];
            if (cell.IsEmpty) continue;
            var faces = TerrainLodFaceMask.None;
            Test(x, y - 1, z, TerrainLodFaceMask.Down);
            Test(x, y + 1, z, TerrainLodFaceMask.Up);
            Test(x, y, z - 1, TerrainLodFaceMask.North);
            Test(x, y, z + 1, TerrainLodFaceMask.South);
            Test(x - 1, y, z, TerrainLodFaceMask.West);
            Test(x + 1, y, z, TerrainLodFaceMask.East);
            level.MutableCells[index] = cell with { ExposedFaces = faces };

            void Test(int neighborX, int neighborY, int neighborZ, TerrainLodFaceMask face)
            {
                if ((uint)neighborX >= (uint)level.Width ||
                    (uint)neighborY >= (uint)level.Height ||
                    (uint)neighborZ >= (uint)level.Depth)
                {
                    faces |= face;
                    return;
                }
                var neighbor = level[neighborX, neighborY, neighborZ];
                if (neighbor.IsEmpty || !neighbor.Primary.OccludesFaces ||
                    cell.Primary.Geometry == TerrainLodGeometryClass.Liquid &&
                    neighbor.Primary != cell.Primary)
                    faces |= face;
            }
        }
    }

    private readonly record struct Candidate(uint Coverage, uint Score);
}
