using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Worlds.Chunks.Storage;

internal static class RegionIo
{
    private static readonly Dictionary<string, RegionFile> cache = new(StringComparer.Ordinal);
    private static readonly object gate = new();

    public static RegionFile CreateRegionFile(string worldDir, int chunkX, int chunkZ)
    {
        if (worldDir is null)
        {
            throw new ArgumentNullException(nameof(worldDir));
        }

        lock (gate)
        {
            var regionDir = Path.Combine(worldDir, "region");
            var regionFileName = $"r.{chunkX >> 5}.{chunkZ >> 5}.mcr";
            var regionPath = Path.Combine(regionDir, regionFileName);

            if (cache.TryGetValue(regionPath, out var region))
            {
                return region;
            }

            if (!Directory.Exists(regionDir))
            {
                Directory.CreateDirectory(regionDir);
            }

            if (cache.Count >= 256)
            {
                Flush();
            }

            RegionFile created = new(regionPath);
            cache[regionPath] = created;
            return created;
        }
    }

    public static void Flush(bool flushToDisk = false)
    {
        lock (gate)
        {
            foreach (var regionFile in cache.Values)
            {
                regionFile.Flush(flushToDisk);
                regionFile.Dispose();
            }

            cache.Clear();
        }
    }

    /// <summary>Flushes cached region files belonging to one dimension without evicting them.</summary>
    public static void FlushWorld(string worldDir, bool flushToDisk)
    {
        var regionDir = Path.GetFullPath(Path.Combine(worldDir, "region")) + Path.DirectorySeparatorChar;
        lock (gate)
        {
            foreach (var (path, regionFile) in cache)
            {
                if (Path.GetFullPath(path).StartsWith(regionDir, StringComparison.Ordinal))
                    regionFile.Flush(flushToDisk);
            }
        }
    }

    public static int GetSizeDelta(string worldDir, int chunkX, int chunkZ)
    {
        var regionFile = CreateRegionFile(worldDir, chunkX, chunkZ);
        return regionFile.func_22209_a();
    }

    public static bool ContainsChunk(string worldDir, int chunkX, int chunkZ)
    {
        if (worldDir is null) throw new ArgumentNullException(nameof(worldDir));
        var regionPath = Path.Combine(
            worldDir,
            "region",
            $"r.{chunkX >> 5}.{chunkZ >> 5}.mcr");
        lock (gate)
        {
            if (!cache.TryGetValue(regionPath, out var regionFile))
            {
                if (!File.Exists(regionPath)) return false;
                regionFile = CreateRegionFile(worldDir, chunkX, chunkZ);
            }
            return regionFile.ContainsChunk(chunkX & 31, chunkZ & 31);
        }
    }

    public static ChunkDataStream? GetChunkInputStream(string worldDir, int chunkX, int chunkZ)
    {
        var regionFile = CreateRegionFile(worldDir, chunkX, chunkZ);
        return regionFile.GetChunkDataInputStream(chunkX & 31, chunkZ & 31);
    }

    public static Stream? GetChunkOutputStream(string worldDir, int chunkX, int chunkZ)
    {
        var regionFile = CreateRegionFile(worldDir, chunkX, chunkZ);
        return regionFile.GetChunkDataOutputStream(chunkX & 31, chunkZ & 31);
    }
}
