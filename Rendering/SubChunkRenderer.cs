using Silk.NET.Maths;
using Silk.NET.OpenGL.Legacy;
using System.Runtime.InteropServices;

namespace betareborn.Rendering
{
    public class SubChunkRenderer : IDisposable
    {
        public const int SIZE = 16;
        public Vector3D<int> Position { get; }
        public Vector3D<int> PositionPlus { get; }
        public Vector3D<int> PositionMinus { get; }
        public Vector3D<int> ClipPosition { get; }
        public AxisAlignedBB BoundingBox { get; }

        private readonly VertexBuffer<Vertex>[] vertexBuffers = new VertexBuffer<Vertex>[2];
        private readonly VertexArray[] vertexArrays = new VertexArray[2];
        private readonly int[] vertexCounts = new int[2];
        private bool disposed = false;

        public SubChunkRenderer(Vector3D<int> position)
        {
            Position = position;

            PositionPlus = new(position.X + SIZE / 2, position.Y + SIZE / 2, position.Z + SIZE / 2);
            ClipPosition = new(position.X & 1023, position.Y, position.Z & 1023);
            PositionMinus = position - ClipPosition;

            const float padding = 6.0f;

            BoundingBox = AxisAlignedBB.getBoundingBox
            (
                position.X - padding,
                position.Y - padding,
                position.Z - padding,
                position.X + SIZE + padding,
                position.Y + SIZE + padding,
                position.Z + SIZE + padding
            );

            vertexCounts[0] = 0;
            vertexCounts[1] = 0;
        }

        public void UploadMeshData(List<Vertex>? solidMesh, List<Vertex>? translucentMesh)
        {
            vertexCounts[0] = 0;
            vertexCounts[1] = 0;

            if (solidMesh != null && solidMesh.Count > 0)
            {
                Span<Vertex> solidMeshData = CollectionsMarshal.AsSpan(solidMesh);
                UploadMesh(vertexBuffers, 0, solidMeshData);
            }

            if (translucentMesh != null && translucentMesh.Count > 0)
            {
                Span<Vertex> translucentMeshData = CollectionsMarshal.AsSpan(translucentMesh);
                UploadMesh(vertexBuffers, 1, translucentMeshData);
            }
        }

        private unsafe void UploadMesh(VertexBuffer<Vertex>[] buffers, int bufferIdx, Span<Vertex> meshData)
        {
            if (buffers[bufferIdx] == null)
            {
                buffers[bufferIdx] = new(meshData);
            }
            else
            {
                buffers[bufferIdx].BufferData(meshData);
            }

            vertexCounts[bufferIdx] = meshData.Length;

            if (vertexArrays[bufferIdx] == null)
            {
                vertexArrays[bufferIdx] = new();
                vertexArrays[bufferIdx].Bind();
                buffers[bufferIdx].Bind();

                GLManager.GL.EnableVertexAttribArray(0);
                GLManager.GL.VertexAttribPointer(0, 3, GLEnum.Float, false, 32, (void*)0);
                GLManager.GL.EnableVertexAttribArray(1);
                GLManager.GL.VertexAttribPointer(1, 2, GLEnum.Float, false, 32, (void*)12);
                GLManager.GL.EnableVertexAttribArray(2);
                GLManager.GL.VertexAttribPointer(2, 4, GLEnum.UnsignedByte, true, 32, (void*)20);

                VertexArray.Unbind();
            }
        }

        public bool HasTranslucentMesh()
        {
            return vertexCounts[1] > 0;
        }

        public unsafe void Render(Shader shader, int pass, Vector3D<double> viewPos, Matrix4X4<float> modelViewMatrix)
        {
            if (pass < 0 || pass > 1)
            {
                throw new ArgumentException("Pass must be 0 or 1");
            }

            int vertexCount = vertexCounts[pass];

            if (vertexCount == 0)
            {
                return;
            }

            Vector3D<double> pos = new(PositionMinus.X - viewPos.X, PositionMinus.Y - viewPos.Y, PositionMinus.Z - viewPos.Z);
            pos += new Vector3D<double>(ClipPosition.X, ClipPosition.Y, ClipPosition.Z);

            modelViewMatrix = Matrix4X4.CreateTranslation(new Vector3D<float>((float)pos.X, (float)pos.Y, (float)pos.Z)) * modelViewMatrix;

            shader.SetUniformMatrix4("modelViewMatrix", modelViewMatrix);

            vertexArrays[pass].Bind();

            GLManager.GL.DrawArrays(GLEnum.Triangles, 0, (uint)vertexCount);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            GC.SuppressFinalize(this);

            vertexBuffers[0]?.Dispose();
            vertexBuffers[1]?.Dispose();

            vertexArrays[0]?.Dispose();
            vertexArrays[1]?.Dispose();

            disposed = true;
        }
    }
}
