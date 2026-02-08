using betareborn.Chunks;
using betareborn.NBT;
using betareborn.Worlds;
using java.io;
using java.lang;

namespace betareborn
{
    public class SaveHandler : ISaveHandler
    {

        private readonly java.io.File saveDirectory;
        private readonly java.io.File playersDirectory;
        private readonly java.io.File field_28114_d;
        private readonly long now = java.lang.System.currentTimeMillis();

        public SaveHandler(java.io.File var1, string var2, bool var3)
        {
            saveDirectory = new java.io.File(var1, var2);
            saveDirectory.mkdirs();
            playersDirectory = new java.io.File(saveDirectory, "players");
            field_28114_d = new java.io.File(saveDirectory, "data");
            field_28114_d.mkdirs();
            if (var3)
            {
                playersDirectory.mkdirs();
            }

            func_22154_d();
        }

        private void func_22154_d()
        {
            try
            {
                java.io.File var1 = new java.io.File(saveDirectory, "session.lock");
                DataOutputStream var2 = new DataOutputStream(new java.io.FileOutputStream(var1));

                try
                {
                    var2.writeLong(now);
                }
                finally
                {
                    var2.close();
                }

            }
            catch (java.io.IOException var7)
            {
                var7.printStackTrace();
                throw new RuntimeException("Failed to check session lock, aborting");
            }
        }

        protected java.io.File getSaveDirectory()
        {
            return saveDirectory;
        }

        public void func_22150_b()
        {
            try
            {
                java.io.File var1 = new java.io.File(saveDirectory, "session.lock");
                DataInputStream var2 = new DataInputStream(new java.io.FileInputStream(var1));

                try
                {
                    if (var2.readLong() != now)
                    {
                        throw new MinecraftException("The save is being accessed from another location, aborting");
                    }
                }
                finally
                {
                    var2.close();
                }

            }
            catch (java.io.IOException var7)
            {
                throw new MinecraftException("Failed to check session lock, aborting");
            }
        }

        public virtual ChunkStorage getChunkLoader(WorldProvider var1)
        {
            if (var1 is WorldProviderHell)
            {
                java.io.File var2 = new java.io.File(saveDirectory, "DIM-1");
                var2.mkdirs();
                return new AlphaChunkStorage(var2, true);
            }
            else
            {
                return new AlphaChunkStorage(saveDirectory, true);
            }
        }

        public WorldInfo loadWorldInfo()
        {
            java.io.File var1 = new java.io.File(saveDirectory, "level.dat");
            NBTTagCompound var2;
            NBTTagCompound var3;
            if (var1.exists())
            {
                try
                {
                    var2 = CompressedStreamTools.func_1138_a(new java.io.FileInputStream(var1));
                    var3 = var2.getCompoundTag("Data");
                    WorldInfo wInfo = new(var3);
                    return wInfo;
                }
                catch (java.lang.Exception var5)
                {
                    var5.printStackTrace();
                }
            }

            var1 = new java.io.File(saveDirectory, "level.dat_old");
            if (var1.exists())
            {
                try
                {
                    var2 = CompressedStreamTools.func_1138_a(new java.io.FileInputStream(var1));
                    var3 = var2.getCompoundTag("Data");
                    WorldInfo wInfo = new(var3);
                    return wInfo;
                }
                catch (java.lang.Exception var4)
                {
                    var4.printStackTrace();
                }
            }

            return null;
        }

        public virtual void saveWorldInfoAndPlayer(WorldInfo var1, java.util.List var2)
        {
            NBTTagCompound var3 = var1.getNBTTagCompoundWithPlayer(var2);
            NBTTagCompound var4 = new();
            var4.setTag("Data", var3);

            try
            {
                var saveTask = Task.Run(() =>
                {
                    java.io.File var5 = new java.io.File(saveDirectory, "level.dat_new");
                    java.io.File var6 = new java.io.File(saveDirectory, "level.dat_old");
                    java.io.File var7 = new java.io.File(saveDirectory, "level.dat");
                    CompressedStreamTools.writeGzippedCompoundToOutputStream(var4, new FileOutputStream(var5));
                    if (var6.exists())
                    {
                        var6.delete();
                    }

                    var7.renameTo(var6);
                    if (var7.exists())
                    {
                        var7.delete();
                    }

                    var5.renameTo(var7);
                    if (var5.exists())
                    {
                        var5.delete();
                    }
                });

                AsyncIO.addTask(saveTask);
            }
            catch (System.Exception e)
            {
                System.Console.WriteLine(e);
            }

        }

        public void saveWorldInfo(WorldInfo var1)
        {
            NBTTagCompound var2 = var1.getNBTTagCompound();
            NBTTagCompound var3 = new NBTTagCompound();
            var3.setTag("Data", var2);

            try
            {
                java.io.File var4 = new java.io.File(saveDirectory, "level.dat_new");
                java.io.File var5 = new java.io.File(saveDirectory, "level.dat_old");
                java.io.File var6 = new java.io.File(saveDirectory, "level.dat");
                CompressedStreamTools.writeGzippedCompoundToOutputStream(var3, new java.io.FileOutputStream(var4));
                if (var5.exists())
                {
                    var5.delete();
                }

                var6.renameTo(var5);
                if (var6.exists())
                {
                    var6.delete();
                }

                var4.renameTo(var6);
                if (var4.exists())
                {
                    var4.delete();
                }
            }
            catch (java.lang.Exception var7)
            {
                var7.printStackTrace();
            }

        }

        public java.io.File func_28113_a(string var1)
        {
            return new java.io.File(field_28114_d, var1 + ".dat");
        }
    }

}