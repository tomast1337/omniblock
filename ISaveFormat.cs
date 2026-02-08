using betareborn.Worlds;
using java.util;

namespace betareborn
{
    public interface ISaveFormat
    {
        String func_22178_a();

        ISaveHandler getSaveLoader(String var1, bool var2);

        List func_22176_b();

        void flushCache();

        WorldInfo func_22173_b(String var1);

        void func_22172_c(String var1);

        void func_22170_a(String var1, String var2);

        bool isOldMapFormat(String var1);

        bool convertMapFormat(String var1, LoadingDisplay var2);
    }

}