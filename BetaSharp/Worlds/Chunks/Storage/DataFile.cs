using BetaSharp.Worlds.Storage;
using java.lang;

namespace BetaSharp.Worlds.Chunks.Storage;

internal class DataFile : Comparable
{
    private readonly java.io.File file;
    private readonly int chunkX;
    private readonly int chunkZ;

    public DataFile(java.io.File file)
    {
        this.file = file;
        var match = DataFilenameFilter.ChunkFilePattern().Match(file.getName());
        if (match.Success)
        {
            chunkX = Integer.parseInt(match.Groups[1].Value, 36);
            chunkZ = Integer.parseInt(match.Groups[2].Value, 36);
        }
        else
        {
            chunkX = 0;
            chunkZ = 0;
        }

    }

    public int comp(DataFile file)
    {
        int regionX = chunkX >> 5;
        int otherRegionX = file.chunkX >> 5;
        if (regionX == otherRegionX)
        {
            int regionZ = chunkZ >> 5;
            int otherRegionZ = file.chunkZ >> 5;
            return regionZ - otherRegionZ;
        }
        else
        {
            return regionX - otherRegionX;
        }
    }

    public java.io.File getFile()
    {
        return file;
    }

    public int GetChunkX()
    {
        return chunkX;
    }

    public int GetChunkZ()
    {
        return chunkZ;
    }

    public int CompareTo(object? file)
    {
        return comp((DataFile)file!);
    }
}
