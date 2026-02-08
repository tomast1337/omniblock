namespace betareborn.Client.Textures
{
    public abstract class TexturePackBase : java.lang.Object
    {
        public string texturePackFileName;
        public string firstDescriptionLine;
        public string secondDescriptionLine;
        public string field_6488_d;

        public virtual void func_6482_a()
        {
        }

        public virtual void closeTexturePackFile()
        {
        }

        public virtual void func_6485_a(Minecraft var1)
        {
        }

        public virtual void func_6484_b(Minecraft var1)
        {
        }

        public virtual void bindThumbnailTexture(Minecraft var1)
        {
        }

        public virtual java.io.InputStream getResourceAsStream(string var1)
        {
            return new java.io.ByteArrayInputStream(AssetManager.Instance.getAsset(var1).getBinaryContent());
        }
    }
}