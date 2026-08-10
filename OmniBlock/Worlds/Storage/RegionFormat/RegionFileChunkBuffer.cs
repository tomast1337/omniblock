using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Worlds.Chunks.Storage;

internal sealed class RegionFileChunkBuffer(RegionFile region, int chunkX, int chunkZ) : MemoryStream(8096)
{
    protected override void Dispose(bool disposing)
    {
        try
        {
            if (!disposing)
            {
                return;
            }

            var buffer = ToArray();
            region.Write(chunkX, chunkZ, buffer, buffer.Length);
        }
        finally
        {
            base.Dispose(disposing);
        }
    }
}
