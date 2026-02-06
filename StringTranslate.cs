namespace betareborn
{
    public class StringTranslate : java.lang.Object
    {
        private static readonly StringTranslate instance = new();
        private readonly java.util.Properties translateTable = new();

        private StringTranslate()
        {
            try
            {
                translateTable.load(new java.io.StringReader(AssetManager.Instance.getAsset("lang/en_US.lang").getTextContent()));
                translateTable.load(new java.io.StringReader(AssetManager.Instance.getAsset("lang/stats_US.lang").getTextContent()));
            }
            catch (java.io.IOException err)
            {
                err.printStackTrace();
            }

        }

        public static StringTranslate getInstance()
        {
            return instance;
        }

        public string translateKey(string key)
        {
            return translateTable.getProperty(key, key);
        }

        public string translateKeyFormat(string key, params object[] var2)
        {
            string var3 = translateTable.getProperty(key, key);
            for (int i = 0; i < var2.Length; i++)
            {
                var3 = var3.Replace($"%{i + 1}$s", var2[i].ToString());
            }
            return var3;
        }

        public string translateNamedKey(string key)
        {
            return translateTable.getProperty(key + ".name", "");
        }
    }
}