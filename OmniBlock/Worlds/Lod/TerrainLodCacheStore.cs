using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using OmniBlock.Registries;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Lod;

public sealed record TerrainLodCacheIdentity(
    string WorldFingerprint,
    int Dimension,
    string ContentFingerprint,
    string GeneratorFingerprint,
    int ReductionSchemaVersion,
    string MaterialRulesFingerprint)
{
    public string CompatibilityFingerprint => Hash(
        WorldFingerprint,
        Dimension.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ContentFingerprint,
        GeneratorFingerprint,
        ReductionSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
        MaterialRulesFingerprint);

    public static TerrainLodCacheIdentity FromWorld(
        IWorldContext world,
        TerrainLodMaterialCatalog materials)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(materials);
        var dimension = world.Dimension.Id;
        var generatorProvider = dimension == 0
            ? world.Properties.TerrainType.GeneratorProviderType
            : world.Content.DimensionGeneratorProfiles
                .GetByDimensionId(dimension).GeneratorProviderType;
        return new TerrainLodCacheIdentity(
            Hash("omniblock-terrain-world-v1", world.Seed.ToString(
                System.Globalization.CultureInfo.InvariantCulture)),
            dimension,
            world.Content.Manifest.Fingerprint,
            Hash(
                "omniblock-terrain-generator-v1",
                dimension.ToString(System.Globalization.CultureInfo.InvariantCulture),
                generatorProvider.ToString(),
                world.Properties.TerrainType.Key.ToString(),
                world.Properties.GeneratorOptions ?? string.Empty),
            TerrainLodHierarchy.ReductionSchemaVersion,
            materials.RulesFingerprint);
    }

    /// <summary>
    ///     Identifies hierarchy data reduced from authoritative chunks received from a server.
    ///     The server/world key prevents equal seeds on different servers from sharing entries;
    ///     each record's source fingerprint additionally validates the actual received arrays.
    /// </summary>
    public static TerrainLodCacheIdentity FromClientObservedWorld(
        string serverIdentity,
        long worldSeed,
        int dimension,
        ContentRuntime content,
        TerrainLodMaterialCatalog materials)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverIdentity);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(materials);
        return new TerrainLodCacheIdentity(
            Hash(
                "omniblock-client-observed-world-v1",
                serverIdentity,
                worldSeed.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            dimension,
            content.Manifest.Fingerprint,
            Hash("omniblock-client-observed-generator-v1", serverIdentity),
            TerrainLodHierarchy.ReductionSchemaVersion,
            materials.RulesFingerprint);
    }

    private static string Hash(params string[] values)
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
        {
            foreach (var value in values)
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                writer.Write(bytes.Length);
                writer.Write(bytes);
            }
        }
        return Convert.ToHexStringLower(
            SHA256.HashData(stream.GetBuffer().AsSpan(0, (int)stream.Length)));
    }
}

public enum TerrainLodCacheReadStatus
{
    Hit,
    Missing,
    StaleTerrain,
    Incompatible,
    Corrupt
}

public sealed record TerrainLodCacheReadResult(
    TerrainLodCacheReadStatus Status,
    TerrainLodHierarchy? Hierarchy,
    string? Diagnostic = null,
    TerrainLodLightingSnapshot? Lighting = null);

public enum TerrainLodCacheWriteStatus
{
    Written,
    RejectedRecordTooLarge
}

public sealed record TerrainLodCacheSnapshot(
    int Dimension,
    long MaxBytes,
    long MaxRecordBytes,
    long CurrentBytes,
    int EntryCount,
    long ReadHits,
    long ReadMisses,
    long StaleReads,
    long IncompatibleReads,
    long CorruptReads,
    long Writes,
    long Replacements,
    long Evictions,
    long RejectedOversizeWrites,
    long WriteFailures);

internal enum TerrainLodCacheWriteStage
{
    TemporaryDurable,
    Published
}

/// <summary>
///     Disposable, versioned terrain LOD records. Each chunk is replaced atomically and lives in
///     a directory separate from authoritative region data. Invalid records are cache misses.
/// </summary>
public sealed class TerrainLodCacheStore
{
    private const ulong Magic = 0x31444F4C494E4D4F; // OMNILOD1
    private const int CurrentFormatVersion = 2;
    private const int ChecksumBytes = 32;
    private const int MaxStringBytes = 4096;
    private const int MaxLevels = 16;
    private const int MaxCellsPerLevel = 4_000_000;
    private const int MaxPaletteEntries = ushort.MaxValue;
    private const int LinuxOpenReadOnly = 0;
    private const int LinuxOpenDirectory = 0x10000;

    private readonly object _gate = new();
    private readonly TerrainLodCacheIdentity _identity;
    private readonly DirectoryInfo _dimensionDirectory;
    private readonly long _maxBytes;
    private readonly long _maxRecordBytes;
    private readonly Action<TerrainLodCacheWriteStage>? _stageObserver;
    private long _currentBytes;
    private int _entryCount;
    private long _readHits;
    private long _readMisses;
    private long _staleReads;
    private long _incompatibleReads;
    private long _corruptReads;
    private long _writes;
    private long _replacements;
    private long _evictions;
    private long _rejectedOversizeWrites;
    private long _writeFailures;
    private TerrainLodCacheSnapshot _publishedSnapshot = null!;

    public TerrainLodCacheStore(
        DirectoryInfo root,
        TerrainLodCacheIdentity identity,
        long maxBytes = 4L * 1024 * 1024 * 1024,
        long maxRecordBytes = 64L * 1024 * 1024)
        : this(root, identity, maxBytes, maxRecordBytes, null) { }

    internal TerrainLodCacheStore(
        DirectoryInfo root,
        TerrainLodCacheIdentity identity,
        long maxBytes,
        long maxRecordBytes,
        Action<TerrainLodCacheWriteStage>? stageObserver)
    {
        ArgumentNullException.ThrowIfNull(root);
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        ValidateIdentity(identity);
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (maxRecordBytes <= 0 || maxRecordBytes > maxBytes)
            throw new ArgumentOutOfRangeException(nameof(maxRecordBytes));
        _maxBytes = maxBytes;
        _maxRecordBytes = maxRecordBytes;
        _stageObserver = stageObserver;
        _dimensionDirectory = new DirectoryInfo(
            Path.Combine(root.FullName, $"dimension_{identity.Dimension}"));
        InitializeUsage();
    }

    public TerrainLodCacheReadResult Read(
        int chunkX,
        int chunkZ,
        long terrainRevision,
        string? sourceFingerprint = null)
    {
        lock (_gate)
        {
            var path = GetRecordPath(chunkX, chunkZ);
            if (!File.Exists(path))
            {
                _readMisses++;
                PublishSnapshotLocked();
                return new TerrainLodCacheReadResult(TerrainLodCacheReadStatus.Missing, null);
            }

            try
            {
                var info = new FileInfo(path);
                if (info.Length < sizeof(ulong) + sizeof(int) + ChecksumBytes ||
                    info.Length > _maxRecordBytes)
                    throw new InvalidDataException(
                        $"LOD record length {info.Length} is outside the supported range.");
                var bytes = File.ReadAllBytes(path);
                var result = Deserialize(
                    bytes, chunkX, chunkZ, terrainRevision, sourceFingerprint);
                switch (result.Status)
                {
                    case TerrainLodCacheReadStatus.Hit:
                        _readHits++;
                        break;
                    case TerrainLodCacheReadStatus.StaleTerrain:
                        _staleReads++;
                        break;
                    case TerrainLodCacheReadStatus.Incompatible:
                        _incompatibleReads++;
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unexpected decoded cache status {result.Status}.");
                }
                PublishSnapshotLocked();
                return result;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                          InvalidDataException or EndOfStreamException or
                                          ArgumentException or FormatException or OverflowException)
            {
                _corruptReads++;
                PublishSnapshotLocked();
                return new TerrainLodCacheReadResult(
                    TerrainLodCacheReadStatus.Corrupt,
                    null,
                    error.GetBaseException().Message);
            }
        }
    }

    public TerrainLodCacheWriteStatus Write(TerrainLodConversionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ValidateResult(result);
        var bytes = Serialize(result);
        lock (_gate)
        {
            if (bytes.LongLength > _maxRecordBytes || bytes.LongLength > _maxBytes)
            {
                _rejectedOversizeWrites++;
                PublishSnapshotLocked();
                return TerrainLodCacheWriteStatus.RejectedRecordTooLarge;
            }

            var finalPath = GetRecordPath(result.ChunkX, result.ChunkZ);
            var finalDirectory = Directory.GetParent(finalPath)!;
            finalDirectory.Create();
            var previousLength = File.Exists(finalPath) ? new FileInfo(finalPath).Length : 0;
            EvictForWriteLocked(finalPath, previousLength, bytes.LongLength);
            var temporaryPath = $"{finalPath}.tmp-{Guid.NewGuid():N}";
            try
            {
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           64 * 1024,
                           FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                _stageObserver?.Invoke(TerrainLodCacheWriteStage.TemporaryDurable);
                File.Move(temporaryPath, finalPath, overwrite: true);
                FlushDirectory(finalDirectory.FullName);
                _stageObserver?.Invoke(TerrainLodCacheWriteStage.Published);

                _currentBytes = checked(_currentBytes - previousLength + bytes.LongLength);
                if (previousLength == 0) _entryCount++;
                else _replacements++;
                _writes++;
                PublishSnapshotLocked();
                return TerrainLodCacheWriteStatus.Written;
            }
            catch
            {
                _writeFailures++;
                PublishSnapshotLocked();
                throw;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // Abandoned temporary files are ignored and removed when the cache reopens.
                }
            }
        }
    }

    public TerrainLodCacheSnapshot Snapshot() => Volatile.Read(ref _publishedSnapshot);

    private byte[] Serialize(TerrainLodConversionResult result)
    {
        using MemoryStream bodyStream = new();
        using (var writer = new BinaryWriter(bodyStream, Encoding.UTF8, leaveOpen: true))
        {
            var palette = BuildPalette(result.Hierarchy);
            var paletteIndices = palette
                .Select(static (material, index) => (material, index))
                .ToDictionary(static pair => pair.material, static pair => (ushort)pair.index);
            writer.Write(Magic);
            writer.Write(CurrentFormatVersion);
            WriteIdentity(writer, _identity);
            writer.Write(result.ChunkX);
            writer.Write(result.ChunkZ);
            writer.Write(result.TerrainRevision);
            WriteString(writer, result.SourceFingerprint ?? string.Empty);
            writer.Write(result.Lighting is not null);
            if (result.Lighting is { } lighting)
            {
                writer.Write(lighting.HasSkyLight);
                writer.Write(lighting.SkyLight.Length);
                writer.Write(lighting.SkyLight);
                writer.Write(lighting.BlockLight.Length);
                writer.Write(lighting.BlockLight);
            }
            writer.Write((byte)result.Hierarchy.Strategy);
            WriteString(writer, result.Hierarchy.CanonicalHash);
            writer.Write(palette.Count);
            foreach (var material in palette) WriteMaterial(writer, material);
            writer.Write(result.Hierarchy.Levels.Count);
            foreach (var level in result.Hierarchy.Levels)
            {
                writer.Write(level.Level);
                writer.Write(level.Width);
                writer.Write(level.Height);
                writer.Write(level.Depth);
                writer.Write(level.CellCount);
                for (var x = 0; x < level.Width; x++)
                for (var z = 0; z < level.Depth; z++)
                for (var y = 0; y < level.Height; y++)
                {
                    var cell = level[x, y, z];
                    writer.Write(paletteIndices[NormalizeMaterial(cell.Primary)]);
                    writer.Write(paletteIndices[NormalizeMaterial(cell.Secondary)]);
                    writer.Write(paletteIndices[NormalizeMaterial(cell.Tertiary)]);
                    writer.Write(cell.PrimaryCoverage);
                    writer.Write(cell.SecondaryCoverage);
                    writer.Write(cell.TertiaryCoverage);
                    writer.Write(cell.OccupancyMask);
                    writer.Write((byte)cell.ExposedFaces);
                }
            }
        }
        var body = bodyStream.ToArray();
        var checksum = SHA256.HashData(body);
        var record = new byte[checked(body.Length + checksum.Length)];
        body.CopyTo(record, 0);
        checksum.CopyTo(record, body.Length);
        return record;
    }

    private TerrainLodCacheReadResult Deserialize(
        byte[] record,
        int expectedChunkX,
        int expectedChunkZ,
        long expectedTerrainRevision,
        string? expectedSourceFingerprint)
    {
        var bodyLength = record.Length - ChecksumBytes;
        if (bodyLength <= 0 || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(record.AsSpan(0, bodyLength)),
                record.AsSpan(bodyLength, ChecksumBytes)))
            throw new InvalidDataException("LOD cache record checksum does not match.");

        using MemoryStream stream = new(record, 0, bodyLength, writable: false);
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: false);
        if (reader.ReadUInt64() != Magic)
            throw new InvalidDataException("LOD cache record has an invalid signature.");
        var format = reader.ReadInt32();
        if (format != CurrentFormatVersion)
            return new TerrainLodCacheReadResult(
                TerrainLodCacheReadStatus.Incompatible,
                null,
                $"Cache format {format} is not supported by format {CurrentFormatVersion}.");
        var identity = ReadIdentity(reader);
        if (identity != _identity)
            return new TerrainLodCacheReadResult(
                TerrainLodCacheReadStatus.Incompatible,
                null,
                $"Cache identity {identity.CompatibilityFingerprint} does not match " +
                $"{_identity.CompatibilityFingerprint}.");

        var chunkX = reader.ReadInt32();
        var chunkZ = reader.ReadInt32();
        if (chunkX != expectedChunkX || chunkZ != expectedChunkZ)
            throw new InvalidDataException(
                $"LOD record for {expectedChunkX},{expectedChunkZ} contains {chunkX},{chunkZ}.");
        var terrainRevision = reader.ReadInt64();
        if (terrainRevision != expectedTerrainRevision)
            return new TerrainLodCacheReadResult(
                TerrainLodCacheReadStatus.StaleTerrain,
                null,
                $"Cached terrain revision {terrainRevision} does not match {expectedTerrainRevision}.");
        var sourceFingerprint = ReadString(reader);
        if (expectedSourceFingerprint is not null &&
            !string.Equals(sourceFingerprint, expectedSourceFingerprint, StringComparison.Ordinal))
            return new TerrainLodCacheReadResult(
                TerrainLodCacheReadStatus.StaleTerrain,
                null,
                $"Cached terrain source {sourceFingerprint} does not match " +
                $"{expectedSourceFingerprint}.");

        TerrainLodLightingSnapshot? lighting = null;
        if (reader.ReadBoolean())
        {
            var hasSkyLight = reader.ReadBoolean();
            var skyLight = ReadPackedLight(reader, "sky");
            var blockLight = ReadPackedLight(reader, "block");
            lighting = new TerrainLodLightingSnapshot(
                chunkX, chunkZ, terrainRevision, skyLight, blockLight, hasSkyLight);
        }

        var strategyValue = reader.ReadByte();
        if (!Enum.IsDefined(typeof(TerrainLodReductionStrategy), strategyValue))
            throw new InvalidDataException($"Unknown terrain LOD reduction strategy {strategyValue}.");
        var strategy = (TerrainLodReductionStrategy)strategyValue;
        var expectedHash = ReadString(reader);
        var paletteCount = reader.ReadInt32();
        if (paletteCount is <= 0 or > MaxPaletteEntries)
            throw new InvalidDataException(
                $"LOD record has invalid material palette size {paletteCount}.");
        var palette = new TerrainLodMaterial[paletteCount];
        var uniqueMaterials = new HashSet<TerrainLodMaterial>();
        for (var index = 0; index < palette.Length; index++)
        {
            palette[index] = ReadMaterial(reader);
            if (!uniqueMaterials.Add(palette[index]))
                throw new InvalidDataException(
                    $"LOD material palette repeats entry {palette[index]}.");
        }
        var levelCount = reader.ReadInt32();
        if (levelCount is <= 0 or > MaxLevels)
            throw new InvalidDataException($"LOD record has invalid level count {levelCount}.");
        var levels = new TerrainLodLevel[levelCount];
        for (var index = 0; index < levelCount; index++)
        {
            var levelNumber = reader.ReadInt32();
            var width = reader.ReadInt32();
            var height = reader.ReadInt32();
            var depth = reader.ReadInt32();
            var cellCount = reader.ReadInt32();
            ValidateLevel(levels, index, levelNumber, width, height, depth, cellCount);
            var cells = new TerrainLodCell[cellCount];
            var level = new TerrainLodLevel(levelNumber, width, height, depth, cells);
            for (var x = 0; x < width; x++)
            for (var z = 0; z < depth; z++)
            for (var y = 0; y < height; y++)
            {
                var primary = ReadPaletteMaterial(reader, palette);
                var secondary = ReadPaletteMaterial(reader, palette);
                var tertiary = ReadPaletteMaterial(reader, palette);
                var cell = new TerrainLodCell(
                    primary,
                    secondary,
                    tertiary,
                    reader.ReadUInt32(),
                    reader.ReadUInt32(),
                    reader.ReadUInt32(),
                    reader.ReadByte(),
                    (TerrainLodFaceMask)reader.ReadByte());
                if ((cell.ExposedFaces & ~TerrainLodFaceMask.All) != 0)
                    throw new InvalidDataException("LOD cell contains an invalid exposed-face mask.");
                cells[level.Index(x, y, z)] = cell;
            }
            levels[index] = level;
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("LOD cache record contains trailing data.");
        var hierarchy = new TerrainLodHierarchy(
            chunkX, chunkZ, terrainRevision, strategy, levels);
        if (!string.Equals(hierarchy.CanonicalHash, expectedHash, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"LOD hierarchy hash {hierarchy.CanonicalHash} does not match {expectedHash}.");
        return new TerrainLodCacheReadResult(
            TerrainLodCacheReadStatus.Hit, hierarchy, Lighting: lighting);
    }

    private static byte[] ReadPackedLight(BinaryReader reader, string channel)
    {
        var expected = ChuckFormat.ChunkSize / 2;
        var length = reader.ReadInt32();
        if (length != expected)
            throw new InvalidDataException(
                $"LOD {channel}-light channel contains {length} bytes; expected {expected}.");
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException(
                $"LOD {channel}-light channel ended after {bytes.Length} of {length} bytes.");
        return bytes;
    }

    private void EvictForWriteLocked(string targetPath, long previousLength, long incomingLength)
    {
        var projected = checked(_currentBytes - previousLength + incomingLength);
        if (projected <= _maxBytes) return;
        var candidates = _dimensionDirectory.Exists
            ? _dimensionDirectory.EnumerateFiles("*.olod", SearchOption.AllDirectories)
                .Where(file => !string.Equals(file.FullName, targetPath, StringComparison.Ordinal))
                .OrderBy(static file => file.LastWriteTimeUtc)
                .ThenBy(static file => file.FullName, StringComparer.Ordinal)
                .ToArray()
            : [];
        foreach (var candidate in candidates)
        {
            if (projected <= _maxBytes) break;
            var length = candidate.Length;
            candidate.Delete();
            _currentBytes -= length;
            _entryCount--;
            _evictions++;
            projected -= length;
        }
        if (projected > _maxBytes)
            throw new InvalidOperationException(
                $"LOD record requires {incomingLength} bytes but cache budget is {_maxBytes} bytes.");
    }

    private void InitializeUsage()
    {
        if (_dimensionDirectory.Exists)
        {
            foreach (var temporary in _dimensionDirectory.EnumerateFiles(
                         "*.tmp-*", SearchOption.AllDirectories))
                temporary.Delete();
            foreach (var file in _dimensionDirectory.EnumerateFiles(
                         "*.olod", SearchOption.AllDirectories))
            {
                _currentBytes = checked(_currentBytes + file.Length);
                _entryCount++;
            }
        }
        PublishSnapshotLocked();
    }

    private string GetRecordPath(int chunkX, int chunkZ)
    {
        var regionX = FloorDiv(chunkX, 32);
        var regionZ = FloorDiv(chunkZ, 32);
        return Path.Combine(
            _dimensionDirectory.FullName,
            $"region_{regionX}_{regionZ}",
            $"chunk_{chunkX}_{chunkZ}.olod");
    }

    private static int FloorDiv(int value, int divisor)
    {
        var quotient = value / divisor;
        var remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    private static void ValidateIdentity(TerrainLodCacheIdentity identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.WorldFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.ContentFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.GeneratorFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.MaterialRulesFingerprint);
        if (identity.ReductionSchemaVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(identity.ReductionSchemaVersion));
    }

    private void ValidateResult(TerrainLodConversionResult result)
    {
        if (result.Dimension != _identity.Dimension)
            throw new ArgumentException(
                $"LOD result dimension {result.Dimension} does not match cache dimension " +
                $"{_identity.Dimension}.", nameof(result));
        if (result.Hierarchy.ChunkX != result.ChunkX ||
            result.Hierarchy.ChunkZ != result.ChunkZ ||
            result.Hierarchy.TerrainRevision != result.TerrainRevision)
            throw new ArgumentException("LOD result and hierarchy coordinates/revision disagree.",
                nameof(result));
        if (result.Lighting is { } lighting &&
            (lighting.ChunkX != result.ChunkX || lighting.ChunkZ != result.ChunkZ ||
             lighting.TerrainRevision != result.TerrainRevision))
            throw new ArgumentException(
                "LOD result and lighting coordinates/revision disagree.", nameof(result));
        if (_identity.ReductionSchemaVersion != TerrainLodHierarchy.ReductionSchemaVersion)
            throw new InvalidOperationException(
                $"Cache reduction schema {_identity.ReductionSchemaVersion} does not match runtime " +
                $"schema {TerrainLodHierarchy.ReductionSchemaVersion}.");
    }

    private static void ValidateLevel(
        TerrainLodLevel[] levels,
        int index,
        int level,
        int width,
        int height,
        int depth,
        int cellCount)
    {
        if (level != index || width <= 0 || height <= 0 || depth <= 0)
            throw new InvalidDataException($"LOD level {index} has invalid dimensions or index.");
        var expectedCells = checked(width * height * depth);
        if (cellCount != expectedCells || cellCount > MaxCellsPerLevel)
            throw new InvalidDataException(
                $"LOD level {index} declares {cellCount} cells; expected {expectedCells}.");
        if (index == 0) return;
        var previous = levels[index - 1];
        if (width != (previous.Width + 1) / 2 ||
            height != (previous.Height + 1) / 2 ||
            depth != (previous.Depth + 1) / 2)
            throw new InvalidDataException(
                $"LOD level {index} is not the parent of level {index - 1}.");
    }

    private static void WriteIdentity(BinaryWriter writer, TerrainLodCacheIdentity identity)
    {
        WriteString(writer, identity.WorldFingerprint);
        writer.Write(identity.Dimension);
        WriteString(writer, identity.ContentFingerprint);
        WriteString(writer, identity.GeneratorFingerprint);
        writer.Write(identity.ReductionSchemaVersion);
        WriteString(writer, identity.MaterialRulesFingerprint);
    }

    private static TerrainLodCacheIdentity ReadIdentity(BinaryReader reader) => new(
        ReadString(reader),
        reader.ReadInt32(),
        ReadString(reader),
        ReadString(reader),
        reader.ReadInt32(),
        ReadString(reader));

    private static void WriteMaterial(BinaryWriter writer, TerrainLodMaterial material)
    {
        WriteString(writer, material.BlockId?.ToString() ?? TerrainLodMaterial.Air.BlockId.ToString());
        writer.Write(material.Metadata);
        writer.Write((byte)material.Geometry);
        writer.Write(material.OccludesFaces);
        writer.Write(material.MapColor);
    }

    private static TerrainLodMaterial ReadMaterial(BinaryReader reader)
    {
        var id = ResourceLocation.Parse(ReadString(reader));
        var metadata = reader.ReadByte();
        var geometryValue = reader.ReadByte();
        if (!Enum.IsDefined(typeof(TerrainLodGeometryClass), geometryValue))
            throw new InvalidDataException($"Unknown terrain LOD geometry class {geometryValue}.");
        return new TerrainLodMaterial(
            id,
            metadata,
            (TerrainLodGeometryClass)geometryValue,
            reader.ReadBoolean(),
            reader.ReadUInt32());
    }

    private static IReadOnlyList<TerrainLodMaterial> BuildPalette(TerrainLodHierarchy hierarchy)
    {
        var materials = new HashSet<TerrainLodMaterial> { TerrainLodMaterial.Air };
        foreach (var level in hierarchy.Levels)
        for (var x = 0; x < level.Width; x++)
        for (var z = 0; z < level.Depth; z++)
        for (var y = 0; y < level.Height; y++)
        {
            var cell = level[x, y, z];
            materials.Add(NormalizeMaterial(cell.Primary));
            materials.Add(NormalizeMaterial(cell.Secondary));
            materials.Add(NormalizeMaterial(cell.Tertiary));
        }
        if (materials.Count > MaxPaletteEntries)
            throw new InvalidDataException(
                $"LOD hierarchy uses {materials.Count} materials; maximum is {MaxPaletteEntries}.");
        return materials
            .OrderBy(static material => material.BlockId)
            .ThenBy(static material => material.Metadata)
            .ThenBy(static material => material.Geometry)
            .ThenBy(static material => material.OccludesFaces)
            .ThenBy(static material => material.MapColor)
            .ToArray();
    }

    private static TerrainLodMaterial NormalizeMaterial(TerrainLodMaterial material) =>
        material.BlockId is null ? TerrainLodMaterial.Air : material;

    private static TerrainLodMaterial ReadPaletteMaterial(
        BinaryReader reader,
        IReadOnlyList<TerrainLodMaterial> palette)
    {
        var index = reader.ReadUInt16();
        if (index >= palette.Count)
            throw new InvalidDataException(
                $"LOD cell references material palette index {index} of {palette.Count}.");
        return palette[index];
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > MaxStringBytes)
            throw new InvalidDataException(
                $"LOD cache string is {bytes.Length} bytes; maximum is {MaxStringBytes}.");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length is < 0 or > MaxStringBytes)
            throw new InvalidDataException($"LOD cache string length {length} is invalid.");
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }

    private void PublishSnapshotLocked() => Volatile.Write(ref _publishedSnapshot,
        new TerrainLodCacheSnapshot(
            _identity.Dimension,
            _maxBytes,
            _maxRecordBytes,
            _currentBytes,
            _entryCount,
            _readHits,
            _readMisses,
            _staleReads,
            _incompatibleReads,
            _corruptReads,
            _writes,
            _replacements,
            _evictions,
            _rejectedOversizeWrites,
            _writeFailures));

    private static void FlushDirectory(string path)
    {
        if (!OperatingSystem.IsLinux()) return;
        var descriptor = LinuxOpen(path, LinuxOpenReadOnly | LinuxOpenDirectory);
        if (descriptor < 0)
            throw new IOException(
                $"Could not open terrain LOD cache directory '{path}' for durable flush.",
                new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        try
        {
            if (LinuxFsync(descriptor) != 0)
                throw new IOException(
                    $"Could not durably flush terrain LOD cache directory '{path}'.",
                    new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        }
        finally
        {
            LinuxClose(descriptor);
        }
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int LinuxOpen(string path, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int LinuxFsync(int descriptor);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int LinuxClose(int descriptor);
}

public sealed record TerrainLodCacheWriterSnapshot(
    long WriteAttempts,
    long Written,
    long CacheHitsAcknowledged,
    long Rejected,
    long Failures,
    string? LastFailure);

/// <summary>Durably consumes conversion results without losing them on transient write failure.</summary>
public sealed class TerrainLodCacheWriter
{
    private readonly object _gate = new();
    private readonly TerrainLodConversionService _conversions;
    private readonly Func<TerrainLodConversionResult, TerrainLodCacheWriteStatus> _write;
    private long _writeAttempts;
    private long _written;
    private long _cacheHitsAcknowledged;
    private long _rejected;
    private long _failures;
    private string? _lastFailure;
    private TerrainLodCacheWriterSnapshot _publishedSnapshot = new(0, 0, 0, 0, 0, null);

    public TerrainLodCacheWriter(
        TerrainLodConversionService conversions,
        TerrainLodCacheStore store)
        : this(conversions, RequireStore(store).Write) { }

    internal TerrainLodCacheWriter(
        TerrainLodConversionService conversions,
        Func<TerrainLodConversionResult, TerrainLodCacheWriteStatus> write)
    {
        _conversions = conversions ?? throw new ArgumentNullException(nameof(conversions));
        _write = write ?? throw new ArgumentNullException(nameof(write));
    }

    public int Drain(int maxRecords)
    {
        if (maxRecords <= 0) throw new ArgumentOutOfRangeException(nameof(maxRecords));
        lock (_gate)
        {
            var consumed = 0;
            while (consumed < maxRecords && _conversions.TryPeekCompleted(out var result))
            {
                if (!result!.RequiresPersistence)
                {
                    _cacheHitsAcknowledged++;
                    _lastFailure = null;
                    _conversions.AcknowledgeCompleted(
                        result.ChunkX, result.ChunkZ, result.TerrainRevision);
                    consumed++;
                    continue;
                }
                _writeAttempts++;
                try
                {
                    var status = _write(result!);
                    if (status == TerrainLodCacheWriteStatus.Written) _written++;
                    else _rejected++;
                    _lastFailure = null;
                    _conversions.AcknowledgeCompleted(
                        result!.ChunkX, result.ChunkZ, result.TerrainRevision);
                    consumed++;
                }
                catch (Exception error)
                {
                    _failures++;
                    _lastFailure = error.GetBaseException().Message;
                    PublishSnapshotLocked();
                    break;
                }
            }
            PublishSnapshotLocked();
            return consumed;
        }
    }

    public TerrainLodCacheWriterSnapshot Snapshot() => Volatile.Read(ref _publishedSnapshot);

    private static TerrainLodCacheStore RequireStore(TerrainLodCacheStore? store) =>
        store ?? throw new ArgumentNullException(nameof(store));

    private void PublishSnapshotLocked() => Volatile.Write(ref _publishedSnapshot,
        new TerrainLodCacheWriterSnapshot(
            _writeAttempts,
            _written,
            _cacheHitsAcknowledged,
            _rejected,
            _failures,
            _lastFailure));
}

public sealed record TerrainLodAsyncCacheWriterSnapshot(
    int Capacity,
    int Queued,
    long Submitted,
    long Coalesced,
    long RejectedAtCapacity,
    long Written,
    long Failed,
    string? LastError);

/// <summary>
///     Best-effort cache sink for consumers that must retain a completed conversion for another
///     purpose, such as client GPU installation. Disk writes never run on the render thread and a
///     saturated queue drops disposable cache work rather than applying backpressure to gameplay.
/// </summary>
public sealed class TerrainLodAsyncCacheWriter : IDisposable
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly TerrainLodCacheStore _store;
    private readonly Dictionary<(int X, int Z), TerrainLodConversionResult> _pending = [];
    private readonly Queue<(int X, int Z)> _order = [];
    private readonly Thread _worker;
    private bool _disposed;
    private long _submitted;
    private long _coalesced;
    private long _rejectedAtCapacity;
    private long _written;
    private long _failed;
    private string? _lastError;
    private TerrainLodAsyncCacheWriterSnapshot _snapshot = null!;

    public TerrainLodAsyncCacheWriter(TerrainLodCacheStore store, int capacity = 32)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        PublishSnapshotLocked();
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = $"TerrainLOD-CacheWriter-{store.Snapshot().Dimension}"
        };
        _worker.Start();
    }

    public bool TrySubmit(TerrainLodConversionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.RequiresPersistence) return false;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var key = (result.ChunkX, result.ChunkZ);
            if (_pending.TryGetValue(key, out var existing))
            {
                if (result.TerrainRevision >= existing.TerrainRevision)
                    _pending[key] = result;
                _coalesced++;
                PublishSnapshotLocked();
                return true;
            }
            if (_pending.Count >= _capacity)
            {
                _rejectedAtCapacity++;
                PublishSnapshotLocked();
                return false;
            }
            _pending.Add(key, result);
            _order.Enqueue(key);
            _submitted++;
            PublishSnapshotLocked();
            Monitor.Pulse(_gate);
            return true;
        }
    }

    public TerrainLodAsyncCacheWriterSnapshot Snapshot() => Volatile.Read(ref _snapshot);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _pending.Clear();
            _order.Clear();
            PublishSnapshotLocked();
            Monitor.PulseAll(_gate);
        }
        _worker.Join(TimeSpan.FromSeconds(5));
    }

    private void WorkerLoop()
    {
        while (true)
        {
            TerrainLodConversionResult result;
            lock (_gate)
            {
                while (!_disposed && _order.Count == 0) Monitor.Wait(_gate);
                if (_disposed) return;
                var key = _order.Dequeue();
                if (!_pending.Remove(key, out result!)) continue;
                PublishSnapshotLocked();
            }

            try
            {
                var status = _store.Write(result);
                lock (_gate)
                {
                    if (status == TerrainLodCacheWriteStatus.Written)
                        _written++;
                    else
                    {
                        _failed++;
                        _lastError = "Terrain LOD cache record exceeded the configured budget.";
                    }
                    PublishSnapshotLocked();
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                          InvalidDataException or ArgumentException or
                                          InvalidOperationException)
            {
                lock (_gate)
                {
                    _failed++;
                    _lastError = error.GetBaseException().Message;
                    PublishSnapshotLocked();
                }
            }
        }
    }

    private void PublishSnapshotLocked() => Volatile.Write(ref _snapshot,
        new TerrainLodAsyncCacheWriterSnapshot(
            _capacity,
            _pending.Count,
            _submitted,
            _coalesced,
            _rejectedAtCapacity,
            _written,
            _failed,
            _lastError));
}
