using java.io;
using java.lang;
using java.util;
using java.util.logging;

namespace betareborn.Server
{
    public class ServerProperties
    {
        public static Logger logger = Logger.getLogger("Minecraft");
        private Properties properties = new Properties();
        private java.io.File propertiesFile;

        public ServerProperties(java.io.File file)
        {
            propertiesFile = file;
            if (file.exists())
            {
                try
                {
                    properties.load(new FileInputStream(file));
                }
                catch (java.lang.Exception var3)
                {
                    logger.log(Level.WARNING, "Failed to load " + file, (Throwable)var3);
                    generateNew();
                }
            }
            else
            {
                logger.log(Level.WARNING, file + " does not exist");
                generateNew();
            }
        }

        public void generateNew()
        {
            logger.log(Level.INFO, "Generating new properties file");
            save();
        }

        public void save()
        {
            try
            {
                properties.store(new FileOutputStream(propertiesFile), "Minecraft server properties");
            }
            catch (java.lang.Exception var2)
            {
                logger.log(Level.WARNING, "Failed to save " + propertiesFile, (Throwable)var2);
                generateNew();
            }
        }

        public string getProperty(string property, string fallback)
        {
            if (!properties.containsKey(property))
            {
                properties.setProperty(property, fallback);
                save();
            }

            return properties.getProperty(property, fallback);
        }

        public int getProperty(string property, int fallback)
        {
            try
            {
                return Integer.parseInt(getProperty(property, "" + fallback));
            }
            catch (java.lang.Exception var4)
            {
                properties.setProperty(property, "" + fallback);
                return fallback;
            }
        }

        public bool getProperty(string property, bool fallback)
        {
            try
            {
                return java.lang.Boolean.parseBoolean(getProperty(property, "" + fallback));
            }
            catch (java.lang.Exception var4)
            {
                properties.setProperty(property, "" + fallback);
                return fallback;
            }
        }

        public void setProperty(string property, bool value)
        {
            properties.setProperty(property, "" + value);
            save();
        }
    }
}
