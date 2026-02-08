using betareborn.Worlds;
using betareborn.Worlds.Chunks.Storage;
using betareborn.Worlds.Dimensions;
using java.util;

namespace betareborn.Worlds.Storage
{

    public class RegionWorldStorage : AlphaWorldStorage
    {

        public RegionWorldStorage(java.io.File var1, string var2, bool var3) : base(var1, var2, var3)
        {
        }

        public override ChunkStorage getChunkStorage(Dimension var1)
        {
            java.io.File var2 = getDirectory();
            if (var1 is NetherDimension)
            {
                java.io.File var3 = new(var2, "DIM-1");
                var3.mkdirs();
                return new RegionChunkStorage(var3);
            }
            else
            {
                return new RegionChunkStorage(var2);
            }
        }

        public override void save(WorldProperties var1, List var2)
        {
            var1.setSaveVersion(19132);
            base.save(var1, var2);
        }
    }

}