using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace OmniBlock.Worlds.Lod;

public enum TerrainLodColumnTileCacheReadStatus
{
    Hit,
    Missing,
    StaleTerrain,
    Incompatible,
    Corrupt
}

public sealed record TerrainLodColumnTileCacheReadResult(
    TerrainLodColumnTileCacheReadStatus Status,
    TerrainLodColumnTile? Tile,
    string? Diagnostic = null);

public enum TerrainLodColumnTileCacheWriteStatus
{
    Written,
    RejectedRecordTooLarge
}

public sealed record TerrainLodColumnTileCacheSnapshot(
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

/// <summary>
///     Versioned storage for canonical cross-chunk column tiles. Records are disposable derived
///     data, bounded independently from the legacy per-chunk hierarchy cache, and replaced by a
///     same-directory rename only after their bytes are durable.
/// </summary>
public sealed class TerrainLodColumnTileCacheStore
{
    private const ulong Magic = 0x314C4F43494E4D4F; // OMNICOL1
    private const int CurrentFormatVersion = 3;
    private const int ChecksumBytes = 32;
    private const int MaximumStringBytes = 4096;
    private const int MaximumSamplesPerSide = 256;
    private const int MaximumWorldHeight = 4096;
    private const int MaximumSpansPerColumn = 4096;
    private const int MaximumTotalSpans = 4_000_000;
    private const int MaximumPaletteEntries = ushort.MaxValue;
    private const int LinuxOpenReadOnly = 0;
    private const int LinuxOpenDirectory = 0x10000;
    private const uint PortableMagic = 0x314C5450; // PTL1
    private const int PortableVersion = 2;

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
    private TerrainLodColumnTileCacheSnapshot _publishedSnapshot = null!;

    public TerrainLodColumnTileCacheStore(
        DirectoryInfo root,
        TerrainLodCacheIdentity identity,
        long maxBytes = 4L * 1024 * 1024 * 1024,
        long maxRecordBytes = 64L * 1024 * 1024)
        : this(root, identity, maxBytes, maxRecordBytes, null) { }

    internal TerrainLodColumnTileCacheStore(
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
        _dimensionDirectory = new DirectoryInfo(Path.Combine(
            root.FullName,
            "spatial_columns",
            $"dimension_{identity.Dimension}"));
        InitializeUsage();
    }

    public TerrainLodColumnTileCacheReadResult Read(
        TerrainLodTileKey key,
        long? expectedLeafTerrainRevision = null,
        string? expectedLeafSourceFingerprint = null)
    {
        if (expectedLeafTerrainRevision.HasValue !=
            (expectedLeafSourceFingerprint is not null))
            throw new ArgumentException(
                "Expected leaf revision and source fingerprint must be supplied together.");
        if (key.Level != 0 && expectedLeafTerrainRevision.HasValue)
            throw new ArgumentException(
                "Source validation applies only to level-zero column tiles.");
        lock (_gate)
        {
            if (key.Level > _identity.MaximumSpatialLevel)
            {
                _incompatibleReads++;
                PublishSnapshotLocked();
                return new TerrainLodColumnTileCacheReadResult(
                    TerrainLodColumnTileCacheReadStatus.Incompatible,
                    null,
                    $"Column tile level {key.Level} exceeds cache manifest maximum " +
                    $"{_identity.MaximumSpatialLevel}.");
            }
            var path = GetRecordPath(key);
            if (!File.Exists(path))
            {
                _readMisses++;
                PublishSnapshotLocked();
                return new TerrainLodColumnTileCacheReadResult(
                    TerrainLodColumnTileCacheReadStatus.Missing, null);
            }

            try
            {
                var info = new FileInfo(path);
                if (info.Length < sizeof(ulong) + sizeof(int) + ChecksumBytes ||
                    info.Length > _maxRecordBytes)
                    throw new InvalidDataException(
                        $"Column-tile record length {info.Length} is outside the supported range.");
                var result = Deserialize(File.ReadAllBytes(path), key);
                if (result.Status == TerrainLodColumnTileCacheReadStatus.Hit &&
                    expectedLeafTerrainRevision is { } revision)
                {
                    var cachedTile = result.Tile!;
                    if (!cachedTile.MatchesLeafSource(
                            revision, expectedLeafSourceFingerprint!))
                        result = new TerrainLodColumnTileCacheReadResult(
                            TerrainLodColumnTileCacheReadStatus.StaleTerrain,
                            null,
                            $"Cached leaf source {cachedTile.InputHashes[0]} does not match " +
                            $"revision {revision} and source {expectedLeafSourceFingerprint}.");
                }
                if (result.Status == TerrainLodColumnTileCacheReadStatus.Hit) _readHits++;
                else if (result.Status == TerrainLodColumnTileCacheReadStatus.StaleTerrain)
                    _staleReads++;
                else if (result.Status == TerrainLodColumnTileCacheReadStatus.Incompatible)
                    _incompatibleReads++;
                else
                    throw new InvalidOperationException(
                        $"Unexpected decoded column-tile status {result.Status}.");
                PublishSnapshotLocked();
                return result;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                          InvalidDataException or EndOfStreamException or
                                          ArgumentException or FormatException or OverflowException)
            {
                _corruptReads++;
                PublishSnapshotLocked();
                return new TerrainLodColumnTileCacheReadResult(
                    TerrainLodColumnTileCacheReadStatus.Corrupt,
                    null,
                    error.GetBaseException().Message);
            }
        }
    }

    public TerrainLodColumnTileCacheWriteStatus Write(TerrainLodColumnTile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        if (tile.Key.Level > _identity.MaximumSpatialLevel)
            throw new InvalidOperationException(
                $"Column tile level {tile.Key.Level} exceeds cache manifest maximum " +
                $"{_identity.MaximumSpatialLevel}.");
        var bytes = Serialize(tile);
        lock (_gate)
        {
            if (bytes.LongLength > _maxRecordBytes || bytes.LongLength > _maxBytes)
            {
                _rejectedOversizeWrites++;
                PublishSnapshotLocked();
                return TerrainLodColumnTileCacheWriteStatus.RejectedRecordTooLarge;
            }

            var finalPath = GetRecordPath(tile.Key);
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
                return TerrainLodColumnTileCacheWriteStatus.Written;
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
                    // Temporary records are ignored and removed when the cache reopens.
                }
            }
        }
    }

    public TerrainLodColumnTileCacheSnapshot Snapshot() =>
        Volatile.Read(ref _publishedSnapshot);

    /// <summary>
    ///     Encodes the identity-independent part of a tile for an already-negotiated content
    ///     session. Disk records additionally carry the complete world/cache identity; the wire
    ///     message stamps the negotiated compatibility fingerprint rather than duplicating these
    ///     fields inside every compressed tile.
    /// </summary>
    internal static byte[] EncodePortable(TerrainLodColumnTile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        ValidateTileForSerialization(tile);
        using MemoryStream stream = new();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            var palette = BuildPalette(tile);
            var paletteIndices = palette
                .Select(static (material, index) => (material, index))
                .ToDictionary(static pair => pair.material, static pair => (ushort)pair.index);
            writer.Write(PortableMagic);
            writer.Write(PortableVersion);
            writer.Write(TerrainLodColumnTile.SchemaVersion);
            writer.Write(tile.Key.Level);
            writer.Write(tile.Key.X);
            writer.Write(tile.Key.Z);
            writer.Write(tile.HorizontalSampleLevel);
            writer.Write(tile.Width);
            writer.Write(tile.WorldHeight);
            writer.Write(tile.LeafTerrainRevision.HasValue);
            if (tile.LeafTerrainRevision is { } revision) writer.Write(revision);
            writer.Write(tile.InputHashes.Count);
            foreach (var input in tile.InputHashes) WriteString(writer, input);
            WriteString(writer, tile.CanonicalHash);
            writer.Write(palette.Count);
            foreach (var material in palette) WriteMaterial(writer, material);
            writer.Write(checked(tile.Width * tile.Width));
            for (var x = 0; x < tile.Width; x++)
            for (var z = 0; z < tile.Width; z++)
            {
                var column = tile[x, z];
                writer.Write(column.Spans.Count);
                foreach (var span in column.Spans)
                {
                    writer.Write(span.BottomY);
                    writer.Write(span.Height);
                    writer.Write(paletteIndices[span.Material]);
                    writer.Write(span.BlockLight);
                    writer.Write(span.SkyLight);
                }
            }
        }
        return stream.ToArray();
    }

    internal static TerrainLodColumnTile DecodePortable(ReadOnlySpan<byte> payload)
    {
        using MemoryStream stream = new(payload.ToArray(), writable: false);
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: false);
        if (reader.ReadUInt32() != PortableMagic)
            throw new InvalidDataException("Terrain LOD wire tile has an invalid signature.");
        if (reader.ReadInt32() != PortableVersion)
            throw new InvalidDataException("Terrain LOD wire tile has an unsupported version.");
        if (reader.ReadInt32() != TerrainLodColumnTile.SchemaVersion)
            throw new InvalidDataException("Terrain LOD wire tile has an unsupported schema.");
        var key = new TerrainLodTileKey(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        var sampleLevel = reader.ReadInt32();
        var width = reader.ReadInt32();
        var worldHeight = reader.ReadInt32();
        ValidateDimensions(key, sampleLevel, width, worldHeight);
        var revision = reader.ReadBoolean() ? reader.ReadInt64() : (long?)null;
        var expectedInputs = key.Level == 0 ? 1 : 4;
        var inputCount = reader.ReadInt32();
        if (inputCount != expectedInputs)
            throw new InvalidDataException(
                $"Terrain LOD wire tile has {inputCount} inputs; expected {expectedInputs}.");
        var inputs = new string[inputCount];
        for (var index = 0; index < inputs.Length; index++) inputs[index] = ReadString(reader);
        var hash = ReadString(reader);
        var paletteCount = reader.ReadInt32();
        if (paletteCount is <= 0 or > MaximumPaletteEntries)
            throw new InvalidDataException("Terrain LOD wire tile has an invalid palette size.");
        var palette = new TerrainLodMaterial[paletteCount];
        var unique = new HashSet<TerrainLodMaterial>();
        for (var index = 0; index < palette.Length; index++)
        {
            palette[index] = ReadMaterial(reader);
            if (!unique.Add(palette[index]))
                throw new InvalidDataException("Terrain LOD wire tile repeats a palette entry.");
        }
        var columnCount = reader.ReadInt32();
        if (columnCount != checked(width * width))
            throw new InvalidDataException("Terrain LOD wire tile has an invalid column count.");
        var columns = new TerrainLodColumn[columnCount];
        var totalSpans = 0;
        for (var index = 0; index < columns.Length; index++)
        {
            var spanCount = reader.ReadInt32();
            if (spanCount is <= 0 or > MaximumSpansPerColumn ||
                checked(totalSpans + spanCount) > MaximumTotalSpans)
                throw new InvalidDataException("Terrain LOD wire tile has an invalid span count.");
            totalSpans += spanCount;
            var spans = new TerrainLodColumnSpan[spanCount];
            for (var spanIndex = 0; spanIndex < spans.Length; spanIndex++)
            {
                var bottomY = reader.ReadInt32();
                var height = reader.ReadInt32();
                var paletteIndex = reader.ReadUInt16();
                if (paletteIndex >= palette.Length)
                    throw new InvalidDataException("Terrain LOD wire tile has an invalid palette reference.");
                spans[spanIndex] = new TerrainLodColumnSpan(
                    bottomY, height, palette[paletteIndex], reader.ReadByte(), reader.ReadByte());
            }
            columns[index] = TerrainLodColumn.Create(worldHeight, spans);
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Terrain LOD wire tile contains trailing data.");
        return TerrainLodColumnTile.FromSerialized(
            key, sampleLevel, width, worldHeight, columns, revision, inputs, hash);
    }

    private byte[] Serialize(TerrainLodColumnTile tile)
    {
        ValidateTileForSerialization(tile);
        using MemoryStream bodyStream = new();
        using (var writer = new BinaryWriter(bodyStream, Encoding.UTF8, leaveOpen: true))
        {
            var palette = BuildPalette(tile);
            var paletteIndices = palette
                .Select(static (material, index) => (material, index))
                .ToDictionary(static pair => pair.material, static pair => (ushort)pair.index);
            writer.Write(Magic);
            writer.Write(CurrentFormatVersion);
            WriteIdentity(writer, _identity);
            writer.Write(TerrainLodColumnTile.SchemaVersion);
            writer.Write(tile.Key.Level);
            writer.Write(tile.Key.X);
            writer.Write(tile.Key.Z);
            writer.Write(tile.HorizontalSampleLevel);
            writer.Write(tile.Width);
            writer.Write(tile.WorldHeight);
            writer.Write(tile.LeafTerrainRevision.HasValue);
            if (tile.LeafTerrainRevision is { } revision) writer.Write(revision);
            writer.Write(tile.InputHashes.Count);
            foreach (var input in tile.InputHashes) WriteString(writer, input);
            WriteString(writer, tile.CanonicalHash);
            writer.Write(palette.Count);
            foreach (var material in palette) WriteMaterial(writer, material);
            writer.Write(checked(tile.Width * tile.Width));
            for (var x = 0; x < tile.Width; x++)
            for (var z = 0; z < tile.Width; z++)
            {
                var column = tile[x, z];
                writer.Write(column.Spans.Count);
                foreach (var span in column.Spans)
                {
                    writer.Write(span.BottomY);
                    writer.Write(span.Height);
                    writer.Write(paletteIndices[span.Material]);
                    writer.Write(span.BlockLight);
                    writer.Write(span.SkyLight);
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

    private TerrainLodColumnTileCacheReadResult Deserialize(
        byte[] record,
        TerrainLodTileKey expectedKey)
    {
        var bodyLength = record.Length - ChecksumBytes;
        if (bodyLength <= 0 || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(record.AsSpan(0, bodyLength)),
                record.AsSpan(bodyLength, ChecksumBytes)))
            throw new InvalidDataException("Column-tile record checksum does not match.");

        using MemoryStream stream = new(record, 0, bodyLength, writable: false);
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: false);
        if (reader.ReadUInt64() != Magic)
            throw new InvalidDataException("Column-tile record has an invalid signature.");
        var format = reader.ReadInt32();
        if (format != CurrentFormatVersion)
            return Incompatible(
                $"Column-tile format {format} is not supported by {CurrentFormatVersion}.");
        var identity = ReadIdentity(reader);
        if (!string.Equals(identity.RecordFingerprint, _identity.RecordFingerprint,
                StringComparison.Ordinal))
            return Incompatible(
                $"Column-tile record identity {identity.RecordFingerprint} does not match " +
                $"{_identity.RecordFingerprint}.");
        var schema = reader.ReadInt32();
        if (schema != TerrainLodColumnTile.SchemaVersion)
            return Incompatible(
                $"Column schema {schema} is not supported by " +
                $"{TerrainLodColumnTile.SchemaVersion}.");

        var key = new TerrainLodTileKey(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
        if (key != expectedKey)
            throw new InvalidDataException(
                $"Column-tile record for {expectedKey} contains {key}.");
        if (key.Level > identity.MaximumSpatialLevel)
            throw new InvalidDataException(
                $"Column-tile record level {key.Level} exceeds its manifest maximum " +
                $"{identity.MaximumSpatialLevel}.");
        var horizontalSampleLevel = reader.ReadInt32();
        var width = reader.ReadInt32();
        var worldHeight = reader.ReadInt32();
        ValidateDimensions(key, horizontalSampleLevel, width, worldHeight);
        var leafRevision = reader.ReadBoolean() ? reader.ReadInt64() : (long?)null;
        var inputCount = reader.ReadInt32();
        var expectedInputs = key.Level == 0 ? 1 : 4;
        if (inputCount != expectedInputs)
            throw new InvalidDataException(
                $"Column tile has {inputCount} inputs; expected {expectedInputs}.");
        var inputHashes = new string[inputCount];
        for (var index = 0; index < inputHashes.Length; index++)
            inputHashes[index] = ReadString(reader);
        var canonicalHash = ReadString(reader);

        var paletteCount = reader.ReadInt32();
        if (paletteCount is <= 0 or > MaximumPaletteEntries)
            throw new InvalidDataException(
                $"Column-tile material palette size {paletteCount} is invalid.");
        var palette = new TerrainLodMaterial[paletteCount];
        var uniqueMaterials = new HashSet<TerrainLodMaterial>();
        for (var index = 0; index < palette.Length; index++)
        {
            palette[index] = ReadMaterial(reader);
            if (!uniqueMaterials.Add(palette[index]))
                throw new InvalidDataException(
                    $"Column-tile palette repeats material {palette[index]}.");
        }

        var columnCount = reader.ReadInt32();
        if (columnCount != checked(width * width))
            throw new InvalidDataException(
                $"Column tile declares {columnCount} columns; expected {width * width}.");
        var columns = new TerrainLodColumn[columnCount];
        var totalSpans = 0;
        for (var index = 0; index < columns.Length; index++)
        {
            var spanCount = reader.ReadInt32();
            if (spanCount is <= 0 or > MaximumSpansPerColumn ||
                checked(totalSpans + spanCount) > MaximumTotalSpans)
                throw new InvalidDataException(
                    $"Column {index} has unsupported span count {spanCount}.");
            totalSpans += spanCount;
            var spans = new TerrainLodColumnSpan[spanCount];
            for (var spanIndex = 0; spanIndex < spans.Length; spanIndex++)
            {
                var bottomY = reader.ReadInt32();
                var height = reader.ReadInt32();
                var paletteIndex = reader.ReadUInt16();
                if (paletteIndex >= palette.Length)
                    throw new InvalidDataException(
                        $"Column {index} references palette index {paletteIndex} of " +
                        $"{palette.Length}.");
                spans[spanIndex] = new TerrainLodColumnSpan(
                    bottomY,
                    height,
                    palette[paletteIndex],
                    reader.ReadByte(),
                    reader.ReadByte());
            }
            columns[index] = TerrainLodColumn.Create(worldHeight, spans);
        }
        if (stream.Position != stream.Length)
            throw new InvalidDataException("Column-tile record contains trailing data.");
        var tile = TerrainLodColumnTile.FromSerialized(
            key,
            horizontalSampleLevel,
            width,
            worldHeight,
            columns,
            leafRevision,
            inputHashes,
            canonicalHash);
        return new TerrainLodColumnTileCacheReadResult(
            TerrainLodColumnTileCacheReadStatus.Hit, tile);

        static TerrainLodColumnTileCacheReadResult Incompatible(string diagnostic) => new(
            TerrainLodColumnTileCacheReadStatus.Incompatible, null, diagnostic);
    }

    private static void ValidateDimensions(
        TerrainLodTileKey key,
        int horizontalSampleLevel,
        int width,
        int worldHeight)
    {
        if (horizontalSampleLevel < 0 || horizontalSampleLevel > key.Level + 4)
            throw new InvalidDataException(
                $"Horizontal sample level {horizontalSampleLevel} is invalid for {key}.");
        var footprintBlocks = checked(16L * key.ChunkWidth);
        var sampleSize = 1L << horizontalSampleLevel;
        var expectedWidth = footprintBlocks / sampleSize;
        if (footprintBlocks % sampleSize != 0 || expectedWidth is <= 0 or > MaximumSamplesPerSide ||
            width != expectedWidth)
            throw new InvalidDataException(
                $"Column tile width {width} is invalid; expected {expectedWidth}.");
        if (worldHeight is <= 0 or > MaximumWorldHeight)
            throw new InvalidDataException(
                $"Column tile world height {worldHeight} is outside 1..{MaximumWorldHeight}.");
    }

    private static void ValidateTileForSerialization(TerrainLodColumnTile tile)
    {
        ValidateDimensions(
            tile.Key, tile.HorizontalSampleLevel, tile.Width, tile.WorldHeight);
        var expectedInputs = tile.Key.Level == 0 ? 1 : 4;
        if (tile.InputHashes.Count != expectedInputs ||
            tile.InputHashes.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException(
                $"Column tile {tile.Key} has {tile.InputHashes.Count} inputs; expected " +
                $"{expectedInputs} non-empty identities.");
        if ((tile.Key.Level == 0) != tile.LeafTerrainRevision.HasValue)
            throw new InvalidDataException(
                "Only level-zero column tiles may carry a terrain revision.");

        var totalSpans = 0;
        for (var x = 0; x < tile.Width; x++)
        for (var z = 0; z < tile.Width; z++)
        {
            var spanCount = tile[x, z].Spans.Count;
            if (spanCount is <= 0 or > MaximumSpansPerColumn ||
                checked(totalSpans + spanCount) > MaximumTotalSpans)
                throw new InvalidDataException(
                    $"Column {x},{z} has unsupported span count {spanCount}.");
            totalSpans += spanCount;
        }
    }

    private static IReadOnlyList<TerrainLodMaterial> BuildPalette(TerrainLodColumnTile tile)
    {
        var materials = new HashSet<TerrainLodMaterial>();
        for (var x = 0; x < tile.Width; x++)
        for (var z = 0; z < tile.Width; z++)
        foreach (var span in tile[x, z].Spans)
            materials.Add(span.Material);
        if (materials.Count is <= 0 or > MaximumPaletteEntries)
            throw new InvalidDataException(
                $"Column tile uses unsupported material count {materials.Count}.");
        return materials
            .OrderBy(static material => material.BlockId)
            .ThenBy(static material => material.Metadata)
            .ThenBy(static material => material.Geometry)
            .ThenBy(static material => material.OccludesFaces)
            .ThenBy(static material => material.MapColor)
            .ThenBy(static material => material.MaxSampleSize)
            .ToArray();
    }

    private void EvictForWriteLocked(string targetPath, long previousLength, long incomingLength)
    {
        var projected = checked(_currentBytes - previousLength + incomingLength);
        if (projected <= _maxBytes) return;
        var candidates = _dimensionDirectory.Exists
            ? _dimensionDirectory.EnumerateFiles("*.ocol", SearchOption.AllDirectories)
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
                $"Column-tile record requires {incomingLength} bytes but cache budget is " +
                $"{_maxBytes} bytes.");
    }

    private void InitializeUsage()
    {
        if (_dimensionDirectory.Exists)
        {
            foreach (var temporary in _dimensionDirectory.EnumerateFiles(
                         "*.tmp-*", SearchOption.AllDirectories))
                temporary.Delete();
            foreach (var file in _dimensionDirectory.EnumerateFiles(
                         "*.ocol", SearchOption.AllDirectories))
            {
                _currentBytes = checked(_currentBytes + file.Length);
                _entryCount++;
            }
        }
        PublishSnapshotLocked();
    }

    private string GetRecordPath(TerrainLodTileKey key)
    {
        var regionX = FloorDivide(key.X, 32);
        var regionZ = FloorDivide(key.Z, 32);
        return Path.Combine(
            _dimensionDirectory.FullName,
            $"level_{key.Level}",
            $"region_{regionX}_{regionZ}",
            $"tile_{key.X}_{key.Z}.ocol");
    }

    private static int FloorDivide(int value, int divisor)
    {
        var quotient = value / divisor;
        return value % divisor < 0 ? quotient - 1 : quotient;
    }

    private static void ValidateIdentity(TerrainLodCacheIdentity identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.WorldFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.ContentFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.GeneratorFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.MaterialRulesFingerprint);
        if (identity.ReductionSchemaVersion <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(identity),
                identity.ReductionSchemaVersion,
                "The LOD reduction schema version must be positive.");
        if (identity.MaximumSpatialLevel is < 0 or
            > TerrainLodSpatialPolicy.MaximumGeneratedSpatialLevel)
            throw new ArgumentOutOfRangeException(
                nameof(identity),
                identity.MaximumSpatialLevel,
                "The cache maximum spatial level is unsupported.");
        if (identity.QualityPolicyVersion !=
            TerrainLodSpatialPolicy.CurrentQualityPolicyVersion)
            throw new ArgumentOutOfRangeException(
                nameof(identity),
                identity.QualityPolicyVersion,
                "The cache quality-policy version is unsupported.");
    }

    private static void WriteIdentity(BinaryWriter writer, TerrainLodCacheIdentity identity)
    {
        WriteString(writer, identity.WorldFingerprint);
        writer.Write(identity.Dimension);
        WriteString(writer, identity.ContentFingerprint);
        WriteString(writer, identity.GeneratorFingerprint);
        writer.Write(identity.ReductionSchemaVersion);
        WriteString(writer, identity.MaterialRulesFingerprint);
        writer.Write(identity.MaximumSpatialLevel);
        writer.Write(identity.QualityPolicyVersion);
    }

    private static TerrainLodCacheIdentity ReadIdentity(BinaryReader reader) => new(
        ReadString(reader),
        reader.ReadInt32(),
        ReadString(reader),
        ReadString(reader),
        reader.ReadInt32(),
        ReadString(reader),
        reader.ReadInt32(),
        reader.ReadInt32());

    private static void WriteMaterial(BinaryWriter writer, TerrainLodMaterial material)
    {
        WriteString(writer, material.BlockId.ToString());
        writer.Write(material.Metadata);
        writer.Write((byte)material.Geometry);
        writer.Write(material.OccludesFaces);
        writer.Write(material.MapColor);
        writer.Write(material.MaxSampleSize);
    }

    private static TerrainLodMaterial ReadMaterial(BinaryReader reader)
    {
        var id = ResourceLocation.Parse(ReadString(reader));
        var metadata = reader.ReadByte();
        var geometryValue = reader.ReadByte();
        if (!Enum.IsDefined(typeof(TerrainLodGeometryClass), geometryValue))
            throw new InvalidDataException(
                $"Unknown terrain LOD geometry class {geometryValue}.");
        return new TerrainLodMaterial(
            id,
            metadata,
            (TerrainLodGeometryClass)geometryValue,
            reader.ReadBoolean(),
            reader.ReadUInt32(),
            reader.ReadInt32());
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > MaximumStringBytes)
            throw new InvalidDataException(
                $"Column-tile string is {bytes.Length} bytes; maximum is {MaximumStringBytes}.");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length is < 0 or > MaximumStringBytes)
            throw new InvalidDataException(
                $"Column-tile string length {length} is invalid.");
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }

    private void PublishSnapshotLocked() => Volatile.Write(ref _publishedSnapshot,
        new TerrainLodColumnTileCacheSnapshot(
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
                $"Could not open column-tile cache directory '{path}' for durable flush.",
                new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        try
        {
            if (LinuxFsync(descriptor) != 0)
                throw new IOException(
                    $"Could not durably flush column-tile cache directory '{path}'.",
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
