using betareborn.NBT;
using java.io;
using java.util.zip;

namespace betareborn
{
    public class NbtIo : java.lang.Object
    {
        public static NBTTagCompound read(InputStream var0)
        {
            DataInputStream var1 = new(new GZIPInputStream(var0));

            NBTTagCompound var2;
            try
            {
                var2 = read((DataInput)var1);
            }
            finally
            {
                var1.close();
            }

            return var2;
        }

        public static void writeGzippedCompoundToOutputStream(NBTTagCompound var0, OutputStream var1)
        {
            DataOutputStream var2 = new(new GZIPOutputStream(var1));

            try
            {
                write(var0, var2);
            }
            finally
            {
                var2.close();
            }

        }

        public static NBTTagCompound read(DataInput var0)
        {
            NBTBase var1 = NBTBase.readTag(var0);
            if (var1 is NBTTagCompound)
            {
                return (NBTTagCompound)var1;
            }
            else
            {
                throw new java.io.IOException("Root tag must be a named compound tag");
            }
        }

        public static void write(NBTTagCompound var0, DataOutput var1)
        {
            NBTBase.writeTag(var0, var1);
        }
    }

}