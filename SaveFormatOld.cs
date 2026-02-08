using betareborn.NBT;
using betareborn.Worlds;
using java.io;
using java.util;

namespace betareborn
{
    public class SaveFormatOld : ISaveFormat
    {

        protected readonly java.io.File field_22180_a;

        public SaveFormatOld(java.io.File var1)
        {
            if (!var1.exists())
            {
                var1.mkdirs();
            }

            this.field_22180_a = var1;
        }

        public virtual String func_22178_a()
        {
            return "Old Format";
        }

        public virtual List func_22176_b()
        {
            ArrayList var1 = new ArrayList();

            for (int var2 = 0; var2 < 5; ++var2)
            {
                String var3 = "World" + (var2 + 1);
                WorldInfo var4 = this.func_22173_b(var3);
                if (var4 != null)
                {
                    var1.add(new SaveFormatComparator(var3, "", var4.getLastTimePlayed(), var4.getSizeOnDisk(), false));
                }
            }

            return var1;
        }

        public virtual void flushCache()
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

        public virtual WorldInfo func_22173_b(String var1)
        {
            java.io.File var2 = new java.io.File(this.field_22180_a, var1);
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
                        var wInfo = new WorldInfo(var5);
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
                        var wInfo = new WorldInfo(var5);
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

        public void func_22170_a(String var1, String var2)
        {
            java.io.File var3 = new java.io.File(this.field_22180_a, var1);
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

        public void func_22172_c(String var1)
        {
            java.io.File var2 = new java.io.File(this.field_22180_a, var1);
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

        public virtual ISaveHandler getSaveLoader(String var1, bool var2)
        {
            return new SaveHandler(this.field_22180_a, var1, var2);
        }

        public virtual bool isOldMapFormat(String var1)
        {
            return false;
        }

        public virtual bool convertMapFormat(String var1, LoadingDisplay var2)
        {
            return false;
        }
    }

}