using BetaSharp.Util.Maths;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Network.Chunks;

/// <summary>
///     A client's on-disk store of chunk blobs it has already received, so a server can skip sending
///     a chunk that has not changed since last time.
///     <para>
///         <c>docs/network-rewrite.md</c> §5.4 item 4. Rejoining a world you have explored currently
///         re-sends every chunk in full; with this it costs one hash comparison per chunk. It is the
///         largest single saving in that section — larger than the palette encoding, which took the
///         same traffic down by a quarter, because this takes it to zero.
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
    ///     Chunk coordinates, hash, and where the payload lives. Sixteen bytes of header plus the
    ///     four-byte length precede every payload on disk.
    /// </summary>
    private const int RecordHeaderBytes = (sizeof(int) * 2) + sizeof(ulong) + sizeof(int);

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
            if (_deadBytes > 0 && _deadBytes >= LiveBytes() * CompactionRatio)
            {
                Compact();
            }

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

        long offset = 0;
        long length = _file.Length;
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
