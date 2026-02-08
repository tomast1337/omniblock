using betareborn.NBT;
using java.io;
using java.util;

namespace betareborn.Worlds.Storage
{
    public class AlphaWorldStorageSource : WorldStorageSource
    {

        protected readonly java.io.File dir;

        public AlphaWorldStorageSource(java.io.File var1)
        {
            if (!var1.exists())
            {
                var1.mkdirs();
            }

            dir = var1;
        }

        public virtual string getName()
        {
            return "Old Format";
        }

        public virtual List getAll()
        {
            ArrayList var1 = new ArrayList();

            for (int var2 = 0; var2 < 5; ++var2)
            {
                string var3 = "World" + (var2 + 1);
                WorldProperties var4 = getProperties(var3);
                if (var4 != null)
                {
                    var1.add(new WorldSaveInfo(var3, "", var4.getLastTimePlayed(), var4.getSizeOnDisk(), false));
                }
            }

            return var1;
        }

        public virtual void flush()
        {
        }

        private static long getFolderSizeMB(java.io.File folder)
        {
            long totalSize = 0;
            java.io.File[] files = folder.listFiles();

            if (files != null)
            {
                foreach (java.io.File file in files)
                {
                    if (file.isFile())
                    {
                        totalSize += file.length();
                    }
                    else if (file.isDirectory())
                    {
                        totalSize += getFolderSizeMB(file);
                    }
                }
            }

            return totalSize;
        }

        public virtual WorldProperties getProperties(string var1)
        {
            java.io.File var2 = new java.io.File(dir, var1);
            if (!var2.exists())
            {
                return null;
            }
            else
            {
                java.io.File var3 = new java.io.File(var2, "level.dat");
                NBTTagCompound var4;
                NBTTagCompound var5;
                if (var3.exists())
                {
                    try
                    {
                        var4 = CompressedStreamTools.func_1138_a(new FileInputStream(var3));
                        var5 = var4.getCompoundTag("Data");
                        long sizeOnDisk = getFolderSizeMB(var2);
                        var wInfo = new WorldProperties(var5);
                        wInfo.setSizeOnDisk(sizeOnDisk);
                        return wInfo;
                    }
                    catch (java.lang.Exception var7)
                    {
                        var7.printStackTrace();
                    }
                }

                var3 = new java.io.File(var2, "level.dat_old");
                if (var3.exists())
                {
                    try
                    {
                        var4 = CompressedStreamTools.func_1138_a(new FileInputStream(var3));
                        var5 = var4.getCompoundTag("Data");
                        long sizeOnDisk = getFolderSizeMB(var2);
                        var wInfo = new WorldProperties(var5);
                        wInfo.setSizeOnDisk(sizeOnDisk);
                        return wInfo;
                    }
                    catch (java.lang.Exception var6)
                    {
                        var6.printStackTrace();
                    }
                }

                return null;
            }
        }

        public void rename(string var1, string var2)
        {
            java.io.File var3 = new java.io.File(dir, var1);
            if (var3.exists())
            {
                java.io.File var4 = new java.io.File(var3, "level.dat");
                if (var4.exists())
                {
                    try
                    {
                        NBTTagCompound var5 = CompressedStreamTools.func_1138_a(new FileInputStream(var4));
                        NBTTagCompound var6 = var5.getCompoundTag("Data");
                        var6.setString("LevelName", var2);
                        CompressedStreamTools.writeGzippedCompoundToOutputStream(var5, new FileOutputStream(var4));
                    }
                    catch (java.lang.Exception var7)
                    {
                        var7.printStackTrace();
                    }
                }

            }
        }

        public void delete(string var1)
        {
            java.io.File var2 = new java.io.File(dir, var1);
            if (var2.exists())
            {
                func_22179_a(var2.listFiles());
                var2.delete();
            }
        }

        protected static void func_22179_a(java.io.File[] var0)
        {
            for (int var1 = 0; var1 < var0.Length; ++var1)
            {
                if (var0[var1].isDirectory())
                {
                    func_22179_a(var0[var1].listFiles());
                }

                var0[var1].delete();
            }

        }

        public virtual WorldStorage get(string var1, bool var2)
        {
            return new AlphaWorldStorage(dir, var1, var2);
        }

        public virtual bool needsConversion(string var1)
        {
            return false;
        }

        public virtual bool convert(string var1, LoadingDisplay var2)
        {
            return false;
        }
    }

}