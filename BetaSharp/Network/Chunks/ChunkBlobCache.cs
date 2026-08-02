using BetaSharp.Util.Maths;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Network.Chunks;

/// <summary>
///     A client's on-disk store of chunk blobs it has already received, so a server can skip sending
///     a chunk that has not changed since last time.
///     <para>
///         Rejoining a world you have explored otherwise re-sends every chunk in full; with this it
///         costs one hash comparison per chunk. That is a larger saving than the palette encoding,
///         which took the same traffic down by a quarter, because this takes it to zero.
///     </para>
///     <para>
///         <b>Append-only, with an index rebuilt at open.</b> A record is written at the end and the
///         newest record for a chunk wins, so a write is one sequential append and never a rewrite.
///         The index scan reads only the fixed-size headers and seeks over payloads, so opening a
///         cache costs a seek per stored chunk rather than a read of the whole file. Compaction
///         happens when superseded records outweigh live ones.
///     </para>
///     <para>
///         <b>This is a cache and is allowed to be wrong.</b> Every failure path — a truncated file,
///         a bad record, an unreadable directory — discards the affected entries rather than
///         throwing, because the fallback is a full chunk transfer, which is what would have happened
///         anyway. Nothing here may ever prevent joining a world.
///     </para>
/// </summary>
public sealed class ChunkBlobCache : IDisposable
{
    /// <summary>Guards against a corrupt length turning into a huge allocation.</summary>
    public const int MaxBlobBytes = 1024 * 1024;

    /// <summary>
    ///     Live bytes kept, after which the chunks furthest from <see cref="LastCentre" /> are
    ///     dropped.
    ///     <para>
    ///         At around two kilobytes a chunk this holds roughly sixty thousand of them, which is a
    ///         view distance of 32 several times over and far more than one player explores. It
    ///         exists because there was previously no bound at all: one file per world per server,
    ///         growing for as long as the player keeps visiting. Measured at 118 MB for a single
    ///         world before records were stored compressed.
    ///     </para>
    ///     <para>
    ///         Furthest-first, because the value of an entry is the chance the player comes back to
    ///         it, and the offer is centred on where they logged out. Least-recently-used would need
    ///         a timestamp per record and would answer a worse question.
    ///     </para>
    /// </summary>
    public const long MaxLiveBytes = 128L * 1024 * 1024;

    /// <summary>
    ///     Chunk coordinates, hash, and where the payload lives. Sixteen bytes of header plus the
    ///     four-byte length precede every payload on disk.
    /// </summary>
    private const int RecordHeaderBytes = (sizeof(int) * 2) + sizeof(ulong) + sizeof(int);

    /// <summary>Identifies the file, so a foreign or older one is discarded rather than parsed.</summary>
    private const uint Magic = 0x4F42_4348;   // "OBCH"

    /// <summary>
    ///     Bumped to 2 when records changed from decoded blobs to the compressed bytes as received.
    ///     An older file is discarded rather than misread, which for a cache costs one re-fetch.
    /// </summary>
    private const byte FormatVersion = 2;

    /// <summary>
    ///     Magic, version, three reserved bytes, then the chunk coordinates the player was last at.
    /// </summary>
    private const int FileHeaderBytes = sizeof(uint) + 1 + 3 + (sizeof(int) * 2);

    /// <summary>
    ///     Where the player was when this cache was last written, in chunk coordinates.
    ///     <para>
    ///         Persisted because the offer has to be sent <em>before</em> the server starts streaming
    ///         chunks, and at that point the server has not said where the player is. Rejoining puts
    ///         you where you logged out, so the last centre is the right one — and without it the
    ///         offer either has to wait for a position, by which time the chunks it would have saved
    ///         are already arriving, or list the whole cache regardless of distance.
    ///     </para>
    /// </summary>
    public ChunkPos LastCentre { get; set; }

    /// <summary>
    ///     Compact once superseded bytes exceed live bytes. Below that the wasted space is bounded by
    ///     a factor of two, which for a cache measured in tens of megabytes is not worth the rewrite.
    /// </summary>
    private const double CompactionRatio = 1.0;

    private static readonly ILogger<ChunkBlobCache> s_logger = Log.Instance.For<ChunkBlobCache>();

    private readonly string _path;
    private readonly Dictionary<ChunkPos, Entry> _index = [];
    private FileStream? _file;
    private long _deadBytes;
    private int _writesSinceFlush;

    /// <summary>How many stored chunks between flushes. See <see cref="Write" />.</summary>
    private const int WritesPerFlush = 64;

    private readonly record struct Entry(ulong Hash, long Offset, int Length);

    /// <summary>Chunks currently held.</summary>
    public int Count => _index.Count;

    /// <summary>True when the store could not be opened, in which case every operation is a no-op.</summary>
    public bool Disabled => _file is null;

    private ChunkBlobCache(string path) => _path = path;

    /// <summary>
    ///     Opens, or returns a disabled cache when it cannot. A client that cannot write to its own
    ///     data directory should still be able to play.
    /// </summary>
    public static ChunkBlobCache Open(string path)
    {
        ChunkBlobCache cache = new(path);

        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            cache._file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            cache.BuildIndex();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            s_logger.LogWarning(exception, "Chunk cache at {Path} is unavailable; running without it.", path);
            cache._file?.Dispose();
            cache._file = null;
        }

        return cache;
    }

    /// <summary>The hash held for a chunk, or null.</summary>
    public ulong? HashOf(ChunkPos position) =>
        _index.TryGetValue(position, out Entry entry) ? entry.Hash : null;

    /// <summary>Every chunk held, with its hash. This is what gets advertised to a server.</summary>
    public IEnumerable<KeyValuePair<ChunkPos, ulong>> Entries =>
        _index.Select(pair => new KeyValuePair<ChunkPos, ulong>(pair.Key, pair.Value.Hash));

    /// <summary>
    ///     The stored blob for a chunk, or null when it is absent or unreadable. A read failure
    ///     evicts the entry: the file has diverged from the index and the caller must be told to
    ///     fetch the chunk rather than handed something questionable.
    /// </summary>
    public byte[]? Read(ChunkPos position)
    {
        if (_file is null || !_index.TryGetValue(position, out Entry entry))
        {
            return null;
        }

        try
        {
            byte[] blob = new byte[entry.Length];
            _file.Position = entry.Offset;
            _file.ReadExactly(blob);
            return blob;
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException)
        {
            s_logger.LogWarning(exception, "Chunk cache entry for {Position} is unreadable; dropping it.", position);
            _index.Remove(position);
            return null;
        }
    }

    /// <summary>
    ///     Stores a blob, superseding any previous one for the same chunk. A store that fails is
    ///     dropped silently — the chunk is already applied, and the only cost is re-fetching it next
    ///     session.
    /// </summary>
    public void Write(ChunkPos position, ulong hash, ReadOnlySpan<byte> blob)
    {
        if (_file is null || blob.Length > MaxBlobBytes)
        {
            return;
        }

        try
        {
            if (_index.TryGetValue(position, out Entry previous))
            {
                _deadBytes += RecordHeaderBytes + previous.Length;
            }

            _file.Position = _file.Length;
            long recordStart = _file.Position;

            Span<byte> header = stackalloc byte[RecordHeaderBytes];
            WriteInt(header[..4], position.X);
            WriteInt(header[4..8], position.Z);
            WriteULong(header[8..16], hash);
            WriteInt(header[16..20], blob.Length);

            _file.Write(header);
            _file.Write(blob);

            _index[position] = new Entry(hash, recordStart + RecordHeaderBytes, blob.Length);

            // Periodic, because a FileStream buffers and .NET Core deliberately gave it no
            // finalizer that flushes. Without this, a session that ends any way other than a clean
            // Dispose loses everything it cached — and "any way other than a clean Dispose" includes
            // the client being killed, which is how a game usually stops. Every 64 chunks bounds the
            // loss to about a second of streaming for one write syscall.
            if (++_writesSinceFlush >= WritesPerFlush)
            {
                _writesSinceFlush = 0;
                Flush();
            }
        }
        catch (IOException exception)
        {
            s_logger.LogWarning(exception, "Could not extend the chunk cache; giving up on it.");
            _file.Dispose();
            _file = null;
        }
    }

    /// <summary>Flushes to disk, and compacts first when enough of the file is superseded.</summary>
    public void Flush()
    {
        if (_file is null)
        {
            return;
        }

        try
        {
            long live = LiveBytes();

            if (live > MaxLiveBytes)
            {
                EvictFurthest(live);
                Compact();
            }
            else if (_deadBytes > 0 && _deadBytes >= live * CompactionRatio)
            {
                Compact();
            }

            // The centre goes out with every flush, so an unclean exit loses at most the movement
            // since the last one rather than the whole hint.
            WriteFileHeader();
            _file.Flush();
        }
        catch (IOException exception)
        {
            s_logger.LogWarning(exception, "Could not flush the chunk cache.");
        }
    }

    public void Dispose()
    {
        Flush();
        _file?.Dispose();
        _file = null;
    }

    // ---- internals ----

    /// <summary>
    ///     Drops the entries furthest from <see cref="LastCentre" /> until the live set fits.
    ///     <para>
    ///         Index only — the records stay on disk until <see cref="Compact" /> rewrites the file,
    ///         which is why the caller does both. Dropping them from the index is what makes them
    ///         dead weight for the compactor to leave behind.
    ///     </para>
    /// </summary>
    private void EvictFurthest(long live)
    {
        foreach ((ChunkPos position, Entry entry) in _index
                     .OrderByDescending(pair => Math.Max(
                         Math.Abs(pair.Key.X - LastCentre.X),
                         Math.Abs(pair.Key.Z - LastCentre.Z)))
                     .ToArray())
        {
            if (live <= MaxLiveBytes)
            {
                break;
            }

            live -= RecordHeaderBytes + entry.Length;
            _deadBytes += RecordHeaderBytes + entry.Length;
            _index.Remove(position);
        }

        s_logger.LogInformation("Chunk cache trimmed to {Count} chunks.", _index.Count);
    }

    private long LiveBytes()
    {
        long total = 0;
        foreach (Entry entry in _index.Values)
        {
            total += RecordHeaderBytes + entry.Length;
        }

        return total;
    }

    /// <summary>
    ///     Walks the file's headers, seeking over payloads. The last record for a chunk wins, which
    ///     is what makes appending a valid update.
    ///     <para>
    ///         A record that does not parse ends the scan and truncates the file there. Everything
    ///         before it is intact and useful; a partial record is what a crash mid-append leaves,
    ///         and there is nothing after it worth trying to recover.
    ///     </para>
    /// </summary>
    private void BuildIndex()
    {
        if (_file is null)
        {
            return;
        }

        _index.Clear();
        _deadBytes = 0;

        long length = _file.Length;
        if (!ReadFileHeader(length))
        {
            // A file we did not write, or one from an older format. Discarding it is always safe:
            // the cost is re-fetching chunks, which is what would have happened without a cache.
            _file.SetLength(0);
            WriteFileHeader();
            return;
        }

        long offset = FileHeaderBytes;
        Span<byte> header = stackalloc byte[RecordHeaderBytes];

        while (offset + RecordHeaderBytes <= length)
        {
            _file.Position = offset;
            if (_file.Read(header) != RecordHeaderBytes)
            {
                break;
            }

            int x = ReadInt(header[..4]);
            int z = ReadInt(header[4..8]);
            ulong hash = ReadULong(header[8..16]);
            int blobLength = ReadInt(header[16..20]);

            if (blobLength < 0 || blobLength > MaxBlobBytes ||
                offset + RecordHeaderBytes + blobLength > length)
            {
                break;
            }

            ChunkPos position = new(x, z);
            if (_index.TryGetValue(position, out Entry previous))
            {
                _deadBytes += RecordHeaderBytes + previous.Length;
            }

            _index[position] = new Entry(hash, offset + RecordHeaderBytes, blobLength);
            offset += RecordHeaderBytes + blobLength;
        }

        if (offset < length)
        {
            s_logger.LogInformation(
                "Chunk cache had {Bytes} trailing bytes from an interrupted write; truncating.",
                length - offset);

            _file.SetLength(offset);
        }
    }

    /// <summary>
    ///     Rewrites the file with only live records, via a temporary alongside it so an interrupted
    ///     compaction cannot destroy a cache that was fine.
    /// </summary>
    private void Compact()
    {
        if (_file is null)
        {
            return;
        }

        string temporary = _path + ".compacting";

        try
        {
            using (FileStream destination = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                Span<byte> fileHeader = stackalloc byte[FileHeaderBytes];
                WriteInt(fileHeader[..4], unchecked((int)Magic));
                fileHeader[4] = FormatVersion;
                WriteInt(fileHeader[8..12], LastCentre.X);
                WriteInt(fileHeader[12..16], LastCentre.Z);
                destination.Write(fileHeader);

                Span<byte> header = stackalloc byte[RecordHeaderBytes];

                foreach ((ChunkPos position, Entry entry) in _index)
                {
                    byte[] blob = new byte[entry.Length];
                    _file.Position = entry.Offset;
                    _file.ReadExactly(blob);

                    WriteInt(header[..4], position.X);
                    WriteInt(header[4..8], position.Z);
                    WriteULong(header[8..16], entry.Hash);
                    WriteInt(header[16..20], entry.Length);

                    destination.Write(header);
                    destination.Write(blob);
                }
            }

            _file.Dispose();
            File.Move(temporary, _path, overwrite: true);

            _file = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            BuildIndex();
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException)
        {
            s_logger.LogWarning(exception, "Chunk cache compaction failed; keeping the file as it was.");

            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
                // Nothing useful to do; the leftover is harmless and will be overwritten next time.
            }

            if (_file is null || !_file.CanRead)
            {
                _file?.Dispose();
                _file = null;
            }
        }
    }

    /// <summary>
    ///     Reads and validates the file header. False means the file is not one of ours and should be
    ///     started over; an empty file is initialised rather than rejected.
    /// </summary>
    private bool ReadFileHeader(long length)
    {
        if (_file is null)
        {
            return false;
        }

        if (length == 0)
        {
            WriteFileHeader();
            return true;
        }

        if (length < FileHeaderBytes)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[FileHeaderBytes];
        _file.Position = 0;
        if (_file.Read(header) != FileHeaderBytes)
        {
            return false;
        }

        if ((uint)ReadInt(header[..4]) != Magic || header[4] != FormatVersion)
        {
            return false;
        }

        LastCentre = new ChunkPos(ReadInt(header[8..12]), ReadInt(header[12..16]));
        return true;
    }

    private void WriteFileHeader()
    {
        if (_file is null)
        {
            return;
        }

        Span<byte> header = stackalloc byte[FileHeaderBytes];
        WriteInt(header[..4], unchecked((int)Magic));
        header[4] = FormatVersion;
        WriteInt(header[8..12], LastCentre.X);
        WriteInt(header[12..16], LastCentre.Z);

        _file.Position = 0;
        _file.Write(header);

        if (_file.Length < FileHeaderBytes)
        {
            _file.SetLength(FileHeaderBytes);
        }
    }

    private static void WriteInt(Span<byte> destination, int value)
    {
        destination[0] = (byte)(value >> 24);
        destination[1] = (byte)(value >> 16);
        destination[2] = (byte)(value >> 8);
        destination[3] = (byte)value;
    }

    private static int ReadInt(ReadOnlySpan<byte> source) =>
        (source[0] << 24) | (source[1] << 16) | (source[2] << 8) | source[3];

    private static void WriteULong(Span<byte> destination, ulong value)
    {
        for (int i = 0; i < sizeof(ulong); i++)
        {
            destination[i] = (byte)(value >> (56 - (i * 8)));
        }
    }

    private static ulong ReadULong(ReadOnlySpan<byte> source)
    {
        ulong value = 0;
        for (int i = 0; i < sizeof(ulong); i++)
        {
            value = (value << 8) | source[i];
        }

        return value;
    }
}
