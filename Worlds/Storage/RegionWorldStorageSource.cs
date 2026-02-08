using betareborn.Worlds.Chunks.Storage;
using java.io;
using java.util;
using java.util.zip;

namespace betareborn.Worlds.Storage
{
    public class RegionWorldStorageSource : AlphaWorldStorageSource
    {

        public RegionWorldStorageSource(java.io.File file) : base(file)
        {
        }

        public override string getName()
        {
            return "Scaevolus\' McRegion";
        }

        public override List getAll()
        {
            ArrayList var1 = new ArrayList();
            java.io.File[] var2 = dir.listFiles();
            java.io.File[] var3 = var2;
            int var4 = var2.Length;

            for (int var5 = 0; var5 < var4; ++var5)
            {
                java.io.File var6 = var3[var5];
                if (var6.isDirectory())
                {
                    string var7 = var6.getName();
                    WorldProperties var8 = getProperties(var7);
                    if (var8 != null)
                    {
                        bool var9 = var8.getSaveVersion() != 19132;
                        string var10 = var8.getWorldName();
                        if (var10 == null || MathHelper.stringNullOrLengthZero(var10))
                        {
                            var10 = var7;
                        }

                        var1.add(new WorldSaveInfo(var7, var10, var8.getLastTimePlayed(), var8.getSizeOnDisk(), var9));
                    }
                }
            }

            return var1;
        }

        public override void flush()
        {
            RegionFileCache.flush();
        }

        public override WorldStorage get(string var1, bool var2)
        {
            return new RegionWorldStorage(dir, var1, var2);
        }

        public override bool needsConversion(string var1)
        {
            WorldProperties var2 = getProperties(var1);
            return var2 != null && var2.getSaveVersion() == 0;
        }

        public override bool convert(string var1, LoadingDisplay var2)
        {
            var2.setLoadingProgress(0);
            ArrayList var3 = new ArrayList();
            ArrayList var4 = new ArrayList();
            ArrayList var5 = new ArrayList();
            ArrayList var6 = new ArrayList();
            java.io.File var7 = new java.io.File(dir, var1);
            java.io.File var8 = new java.io.File(var7, "DIM-1");
            java.lang.System.@out.println("Scanning folders...");
            collectData(var7, var3, var4);
            if (var8.exists())
            {
                collectData(var8, var5, var6);
            }

            int var9 = var3.size() + var5.size() + var4.size() + var6.size();
            java.lang.System.@out.println("Total conversion count is " + var9);
            writeDimensionData(var7, var3, 0, var9, var2);
            writeDimensionData(var8, var5, var3.size(), var9, var2);
            WorldProperties var10 = getProperties(var1);
            var10.setSaveVersion(19132);
            WorldStorage var11 = get(var1, false);
            var11.save(var10);
            deleteData(var4, var3.size() + var5.size(), var9, var2);
            if (var8.exists())
            {
                deleteData(var6, var3.size() + var5.size() + var4.size(), var9, var2);
            }

            return true;
        }

        private void collectData(java.io.File var1, ArrayList var2, ArrayList var3)
        {
            DimensionFileFilter var4 = new DimensionFileFilter(null);
            DataFilenameFilter var5 = new DataFilenameFilter(null);
            java.io.File[] var6 = var1.listFiles(var4);
            java.io.File[] var7 = var6;
            int var8 = var6.Length;

            for (int var9 = 0; var9 < var8; ++var9)
            {
                java.io.File var10 = var7[var9];
                var3.add(var10);
                java.io.File[] var11 = var10.listFiles(var4);
                java.io.File[] var12 = var11;
                int var13 = var11.Length;

                for (int var14 = 0; var14 < var13; ++var14)
                {
                    java.io.File var15 = var12[var14];
                    java.io.File[] var16 = var15.listFiles(var5);
                    java.io.File[] var17 = var16;
                    int var18 = var16.Length;

                    for (int var19 = 0; var19 < var18; ++var19)
                    {
                        java.io.File var20 = var17[var19];
                        var2.add(new DataFile(var20));
                    }
                }
            }

        }

        private void writeDimensionData(java.io.File var1, ArrayList var2, int var3, int var4, LoadingDisplay var5)
        {
            Collections.sort(var2);
            byte[] var6 = new byte[4096];
            Iterator var7 = var2.iterator();

            while (var7.hasNext())
            {
                DataFile var8 = (DataFile)var7.next();
                int var9 = var8.getChunkX();
                int var10 = var8.getChunkZ();
                RegionFile var11 = RegionFileCache.func_22193_a(var1, var9, var10);
                if (!var11.func_22202_c(var9 & 31, var10 & 31))
                {
                    try
                    {
                        DataInputStream var12 = new DataInputStream(new GZIPInputStream(new FileInputStream(var8.getFile())));
                        DataOutputStream var13 = var11.getChunkDataOutputStream(var9 & 31, var10 & 31);
                        bool var14 = false;

                        while (true)
                        {
                            int var17 = var12.read(var6);
                            if (var17 == -1)
                            {
                                var13.close();
                                var12.close();
                                break;
                            }

                            var13.write(var6, 0, var17);
                        }
                    }
                    catch (java.io.IOException var15)
                    {
                        var15.printStackTrace();
                    }
                }

                ++var3;
                int var16 = (int)java.lang.Math.round(100.0D * var3 / var4);
                var5.setLoadingProgress(var16);
            }

            RegionFileCache.flush();
        }

        private void deleteData(ArrayList var1, int var2, int var3, LoadingDisplay var4)
        {
            Iterator var5 = var1.iterator();

            while (var5.hasNext())
            {
                java.io.File var6 = (java.io.File)var5.next();
                java.io.File[] var7 = var6.listFiles();
                func_22179_a(var7);
                var6.delete();
                ++var2;
                int var8 = (int)java.lang.Math.round(100.0D * var2 / var3);
                var4.setLoadingProgress(var8);
            }

        }
    }

}