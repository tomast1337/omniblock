using BetaSharp.Network.Chunks;
using BetaSharp.Util.Maths;

namespace BetaSharp.Tests.Network;

/// <summary>
///     <see cref="ChunkBlobCache" />: the client's on-disk store behind §5.4 item 4.
///     <para>
///         Two properties matter and they pull against each other. It must survive being interrupted
///         — a crash mid-append is the normal way a game exits — and it must never be the reason a
///         world fails to load. So every test here that damages the file asserts the cache keeps
///         working, not that it complains.
///     </para>
/// </summary>
public sealed class ChunkBlobCacheTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "omniblock-cache-tests", Guid.NewGuid().ToString("N"));

    private string Path_ => System.IO.Path.Combine(_directory, "chunks.bin");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not worth failing a test over.
        }
    }

    private static byte[] Blob(int seed, int length = 512)
    {
        byte[] blob = new byte[length];
        new Random(seed).NextBytes(blob);
        return blob;
    }

    [Fact]
    public void A_stored_chunk_comes_back()
    {
        using ChunkBlobCache cache = ChunkBlobCache.Open(Path_);
        byte[] blob = Blob(1);

        cache.Write(new ChunkPos(3, -7), 0xDEADBEEFCAFEF00D, blob);

        Assert.Equal(0xDEADBEEFCAFEF00D, cache.HashOf(new ChunkPos(3, -7)));
        Assert.Equal(blob, cache.Read(new ChunkPos(3, -7)));
    }

    [Fact]
    public void An_absent_chunk_reads_as_nothing_rather_than_throwing()
    {
        using ChunkBlobCache cache = ChunkBlobCache.Open(Path_);

        Assert.Null(cache.HashOf(new ChunkPos(0, 0)));
        Assert.Null(cache.Read(new ChunkPos(0, 0)));
    }

    /// <summary>
    ///     The property that makes append-only viable: a second write for the same chunk wins without
    ///     the file being rewritten.
    /// </summary>
    [Fact]
    public void Rewriting_a_chunk_supersedes_the_previous_copy()
    {
        using ChunkBlobCache cache = ChunkBlobCache.Open(Path_);
        ChunkPos position = new(1, 1);

        cache.Write(position, 1, Blob(1));
        cache.Write(position, 2, Blob(2));

        Assert.Equal(1, cache.Count);
        Assert.Equal(2ul, cache.HashOf(position));
        Assert.Equal(Blob(2), cache.Read(position));
    }

    [Fact]
    public void Everything_survives_being_closed_and_reopened()
    {
        using (ChunkBlobCache cache = ChunkBlobCache.Open(Path_))
        {
            for (int i = 0; i < 50; i++)
            {
                cache.Write(new ChunkPos(i, -i), (ulong)i, Blob(i));
            }
        }

        using ChunkBlobCache reopened = ChunkBlobCache.Open(Path_);

        Assert.Equal(50, reopened.Count);
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal((ulong)i, reopened.HashOf(new ChunkPos(i, -i)));
            Assert.Equal(Blob(i), reopened.Read(new ChunkPos(i, -i)));
        }
    }

    /// <summary>
    ///     A crash between writing a record's header and its payload. Everything before the partial
    ///     record is intact and must stay usable; the partial record is discarded and the file
    ///     truncated so the next append starts from a clean end.
    /// </summary>
    [Fact]
    public void A_write_interrupted_mid_record_costs_only_that_record()
    {
        using (ChunkBlobCache cache = ChunkBlobCache.Open(Path_))
        {
            cache.Write(new ChunkPos(0, 0), 10, Blob(1));
            cache.Write(new ChunkPos(1, 0), 20, Blob(2));
        }

        // Append a header claiming a payload that was never written.
        using (FileStream file = new(Path_, FileMode.Append))
        {
            file.Write(new byte[] { 0, 0, 0, 5, 0, 0, 0, 5, 1, 2, 3, 4, 5, 6, 7, 8, 0, 0, 4, 0 });
        }

        using ChunkBlobCache reopened = ChunkBlobCache.Open(Path_);

        Assert.Equal(2, reopened.Count);
        Assert.Equal(Blob(1), reopened.Read(new ChunkPos(0, 0)));
        Assert.Equal(Blob(2), reopened.Read(new ChunkPos(1, 0)));
        Assert.Null(reopened.HashOf(new ChunkPos(5, 5)));

        // And the file is usable again rather than permanently ending in garbage.
        reopened.Write(new ChunkPos(9, 9), 30, Blob(3));
        Assert.Equal(Blob(3), reopened.Read(new ChunkPos(9, 9)));
    }

    /// <summary>
    ///     Compaction reclaims superseded records. Triggered by rewriting one chunk enough times that
    ///     the dead bytes outweigh the live ones.
    /// </summary>
    [Fact]
    public void Compaction_reclaims_superseded_records_without_losing_data()
    {
        ChunkPos churned = new(0, 0);
        ChunkPos stable = new(1, 1);

        using ChunkBlobCache cache = ChunkBlobCache.Open(Path_);

        cache.Write(stable, 99, Blob(99));
        for (int i = 0; i < 20; i++)
        {
            cache.Write(churned, (ulong)i, Blob(i));
        }

        long before = new FileInfo(Path_).Length;
        cache.Flush();
        long after = new FileInfo(Path_).Length;

        Assert.True(after < before, $"compaction did not shrink the file: {before} -> {after}");

        Assert.Equal(2, cache.Count);
        Assert.Equal(Blob(19), cache.Read(churned));
        Assert.Equal(Blob(99), cache.Read(stable));
        Assert.Equal(19ul, cache.HashOf(churned));
    }

    /// <summary>
    ///     A blob larger than the guard is refused rather than stored. The guard exists so a corrupt
    ///     length in the file cannot become a huge allocation on the next open; refusing to write one
    ///     in the first place keeps the two ends consistent.
    /// </summary>
    [Fact]
    public void An_oversized_blob_is_not_stored()
    {
        using ChunkBlobCache cache = ChunkBlobCache.Open(Path_);

        cache.Write(new ChunkPos(0, 0), 1, new byte[ChunkBlobCache.MaxBlobBytes + 1]);

        Assert.Equal(0, cache.Count);
    }

    /// <summary>
    ///     A cache that cannot be opened is disabled, not fatal. The fallback is a full chunk
    ///     transfer, which is exactly what would have happened without a cache at all — nothing here
    ///     may stop someone playing.
    /// </summary>
    [Fact]
    public void An_unopenable_cache_is_disabled_rather_than_fatal()
    {
        // A directory where the file should be: creating the file cannot succeed.
        Directory.CreateDirectory(Path_);

        using ChunkBlobCache cache = ChunkBlobCache.Open(Path_);

        Assert.True(cache.Disabled);
        Assert.Equal(0, cache.Count);
        Assert.Null(cache.Read(new ChunkPos(0, 0)));

        cache.Write(new ChunkPos(0, 0), 1, Blob(1));
        Assert.Equal(0, cache.Count);
    }

    /// <summary>
    ///     The centre survives a session, which is what lets the offer be sent during configuration —
    ///     before the server has said where the player is, and therefore before it starts streaming
    ///     the chunks the offer exists to prevent.
    /// </summary>
    [Fact]
    public void The_last_centre_survives_a_reopen()
    {
        using (ChunkBlobCache cache = ChunkBlobCache.Open(Path_))
        {
            cache.Write(new ChunkPos(0, 0), 1, Blob(1));
            cache.LastCentre = new ChunkPos(-341, 78);
        }

        using ChunkBlobCache reopened = ChunkBlobCache.Open(Path_);

        Assert.Equal(new ChunkPos(-341, 78), reopened.LastCentre);
        Assert.Equal(1, reopened.Count);
    }

    [Fact]
    public void The_last_centre_survives_compaction()
    {
        using ChunkBlobCache cache = ChunkBlobCache.Open(Path_);
        cache.LastCentre = new ChunkPos(12, -34);

        for (int i = 0; i < 20; i++)
        {
            cache.Write(new ChunkPos(0, 0), (ulong)i, Blob(i));
        }

        cache.Flush();

        Assert.Equal(new ChunkPos(12, -34), cache.LastCentre);
        Assert.Equal(Blob(19), cache.Read(new ChunkPos(0, 0)));
    }

    /// <summary>
    ///     A file this build did not write is discarded rather than parsed. Without the magic, any
    ///     file at all decodes as "records until something stops making sense", which for a format
    ///     that is about to be handed chunk payloads is not a good default.
    /// </summary>
    [Fact]
    public void A_file_that_is_not_ours_is_discarded_rather_than_parsed()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(Path_, Enumerable.Range(0, 4096).Select(i => (byte)i).ToArray());

        using ChunkBlobCache cache = ChunkBlobCache.Open(Path_);

        Assert.False(cache.Disabled);
        Assert.Equal(0, cache.Count);

        // And it is usable from there rather than permanently poisoned.
        cache.Write(new ChunkPos(1, 2), 5, Blob(1));
        Assert.Equal(Blob(1), cache.Read(new ChunkPos(1, 2)));
    }

    /// <summary>
    ///     Data must reach disk without a clean shutdown, because a game usually stops by being
    ///     killed. A <see cref="FileStream" /> buffers and .NET Core gave it no finalizer that
    ///     flushes, so relying on <see cref="ChunkBlobCache.Dispose" /> meant an interrupted session
    ///     cached nothing at all.
    /// </summary>
    [Fact]
    public void Chunks_reach_disk_without_a_clean_shutdown()
    {
        // Deliberately not disposed: this models the process going away.
        ChunkBlobCache cache = ChunkBlobCache.Open(Path_);

        for (int i = 0; i < 200; i++)
        {
            cache.Write(new ChunkPos(i, 0), (ulong)i, Blob(i));
        }

        long onDisk = new FileInfo(Path_).Length;
        Assert.True(onDisk > 0, "nothing was flushed");

        // The handle has to go before another opener can have it, which is the same exclusivity that
        // made the missing teardown visible in the first place.
        cache.Dispose();

        using ChunkBlobCache reopened = ChunkBlobCache.Open(Path_);
        Assert.Equal(200, reopened.Count);
    }

    [Fact]
    public void Entries_lists_everything_held_for_advertising()
    {
        using ChunkBlobCache cache = ChunkBlobCache.Open(Path_);

        cache.Write(new ChunkPos(0, 0), 7, Blob(1));
        cache.Write(new ChunkPos(4, 5), 8, Blob(2));

        Dictionary<ChunkPos, ulong> entries = cache.Entries.ToDictionary(e => e.Key, e => e.Value);

        Assert.Equal(2, entries.Count);
        Assert.Equal(7ul, entries[new ChunkPos(0, 0)]);
        Assert.Equal(8ul, entries[new ChunkPos(4, 5)]);
    }
}
