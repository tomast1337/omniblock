namespace BetaSharp.Client.Rendering.Core;

public class GLAllocation
{
    private static readonly List<int> textureNames = new();
    private static readonly object l = new();

    public static void generateTextureNames(Span<int> textureNamesBuffer)
    {
        lock (l)
        {
            uint[] textureIds = new uint[textureNamesBuffer.Length];
            GLManager.GL.GenTextures(textureIds);

            int[] intIds = Array.ConvertAll(textureIds, id => (int)id);
            textureNamesBuffer.CopyTo(intIds);

            for (int index = 0; index < textureNamesBuffer.Length; ++index)
            {
                textureNames.Add(textureNamesBuffer[index]);
            }
        }
    }

    public static void generateBuffersARB(Span<int> vertexBuffers)
    {
        lock (l)
        {
            uint[] bufferIds = new uint[vertexBuffers.Length];
            GLManager.GL.GenBuffers(bufferIds);
            int[] intIds = Array.ConvertAll(bufferIds, id => (int)id);
            vertexBuffers.CopyTo(intIds);
        }
    }

    public static void deleteTextures()
    {
        lock (l)
        {
            if (textureNames.Count > 0)
            {
                uint[] textureIds = new uint[textureNames.Count];
                for (int i = 0; i < textureNames.Count; i++)
                {
                    textureIds[i] = (uint)textureNames[i];
                }
                GLManager.GL.DeleteTextures(textureIds);
            }

            textureNames.Clear();
        }
    }

    public static Memory<byte> createDirectByteBuffer(int capacity)
    {
        lock (l)
        {
            Memory<byte> buffer = new byte[capacity];
            return buffer;
        }
    }

    public static Memory<int> createDirectIntBuffer(int capacity)
    {
        return new int[capacity];
    }

    public static Memory<float> createDirectFloatBuffer(int capacity)
    {
        return new float[capacity];
    }
}
