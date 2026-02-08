using betareborn.Worlds;
using java.util;

namespace betareborn
{

    public class SaveOldDir : SaveHandler
    {

        public SaveOldDir(java.io.File var1, String var2, bool var3) : base(var1, var2, var3)
        {
        }

        public override ChunkStorage getChunkLoader(WorldProvider var1)
        {
            java.io.File var2 = getSaveDirectory();
            if (var1 is WorldProviderHell)
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

        public override void saveWorldInfoAndPlayer(WorldInfo var1, List var2)
        {
            var1.setSaveVersion(19132);
            base.saveWorldInfoAndPlayer(var1, var2);
        }
    }

}