using java.lang;

namespace betareborn
{
    public class EnumOptionsMappingHelper : java.lang.Object
    {
        public static readonly int[] enumOptionsMappingHelperArray = new int[256];

        static EnumOptionsMappingHelper()
        {
            try
            {
                enumOptionsMappingHelperArray[EnumOptions.INVERT_MOUSE.ordinal()] = 1;
            }
            catch (NoSuchFieldError var5)
            {
            }

            try
            {
                enumOptionsMappingHelperArray[EnumOptions.VIEW_BOBBING.ordinal()] = 2;
            }
            catch (NoSuchFieldError var4)
            {
            }

            try
            {
                enumOptionsMappingHelperArray[EnumOptions.MIPMAPS.ordinal()] = 3;
            }
            catch (NoSuchFieldError var3)
            {
            }

            try
            {
                enumOptionsMappingHelperArray[EnumOptions.DEBUG_MODE.ordinal()] = 4;
            }
            catch (NoSuchFieldError var2)
            {
            }
        }
    }

}