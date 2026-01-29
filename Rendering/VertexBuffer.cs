using Silk.NET.OpenGL.Legacy;

namespace betareborn.Rendering
{
    public class VertexBuffer<T> : IDisposable where T : unmanaged
    {
        private uint id = 0;
        private bool disposed = false;

        public VertexBuffer(Span<T> data)
        {
            id = GLManager.GL.GenBuffer();
            GLManager.GL.BindBuffer(GLEnum.ArrayBuffer, id);
            GLManager.GL.BufferData<T>(GLEnum.ArrayBuffer, data, GLEnum.StaticDraw);
        }

        public void Bind()
        {
            if (disposed || id == 0)
            {
                throw new Exception("Attempted to bind invalid VertexBuffer");
            }

            GLManager.GL.BindBuffer(GLEnum.ArrayBuffer, id);
        }

        public unsafe void BufferData(Span<T> data)
        {
            if (id == 0)
            {
                throw new Exception("Attempted to upload data to an invalid VertexBuffer");
            }
            else
            {
                GLManager.GL.BindBuffer(GLEnum.ArrayBuffer, id);
                GLManager.GL.BufferData(GLEnum.ArrayBuffer, (nuint)(data.Length * sizeof(T)), (void*)0, GLEnum.StaticDraw);
                GLManager.GL.BufferData<T>(GLEnum.ArrayBuffer, data, GLEnum.StaticDraw);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            GC.SuppressFinalize(this);

            if (id != 0)
            {
                GLManager.GL.DeleteBuffer(id);
                id = 0;
            }

            disposed = true;
        }
    }
}
