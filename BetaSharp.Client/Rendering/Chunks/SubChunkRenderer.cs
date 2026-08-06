using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.WebGPU;
using BetaSharp.Util;
using BetaSharp.Util.Maths;
using Silk.NET.Maths;
using Silk.NET.WebGPU;
using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;
using Shader = BetaSharp.Client.Rendering.Core.Shader;
using VertexArray = BetaSharp.Client.Rendering.Core.VertexArray;

namespace BetaSharp.Client.Rendering.Chunks;

public class SubChunkRenderer : IDisposable
{
    public const int Size = 16;
    public bool HasTranslucentMesh => vertexCounts[1] > 0;
    public Vector3D<int> Position { get; }
    public Vector3D<int> PositionPlus { get; }
    public Vector3D<int> PositionMinus { get; }
    public Vector3D<int> ClipPosition { get; }
    public Box BoundingBox { get; }

    public float Age { get; private set; } = 0.0f;
    public bool HasFadedIn => Age >= FadeDuration;
    public const float FadeDuration = 1.0f;

    public int SolidMeshSizeBytes => vertexCounts[0] * 16;
    public int TranslucentMeshSizeBytes => vertexCounts[1] * 16;

    public Occlusion.ChunkVisibilityStore VisibilityData;
    public Occlusion.ChunkDirectionMask IncomingDirections;
    public int LastVisibleFrame = -1;

    public SubChunkRenderer? AdjacentDown;
    public SubChunkRenderer? AdjacentUp;
    public SubChunkRenderer? AdjacentNorth;
    public SubChunkRenderer? AdjacentSouth;
    public SubChunkRenderer? AdjacentWest;
    public SubChunkRenderer? AdjacentEast;

    // GL path
    private readonly VertexBuffer<ChunkVertex>[] vertexBuffers = new VertexBuffer<ChunkVertex>[2];
    private readonly VertexArray[] vertexArrays = new VertexArray[2];

    // WebGPU path
    private readonly WgpuMesh?[] _meshes = new WgpuMesh?[2];

    private readonly int[] vertexCounts = new int[2];
    private bool disposed;

    public SubChunkRenderer(Vector3D<int> position)
    {
        Position = position;

        PositionPlus = new(position.X + Size / 2, position.Y + Size / 2, position.Z + Size / 2);
        ClipPosition = new(position.X & 1023, position.Y, position.Z & 1023);
        PositionMinus = position - ClipPosition;

        const float padding = 6.0f;

        BoundingBox = new Box
        (
            position.X - padding,
            position.Y - padding,
            position.Z - padding,
            position.X + Size + padding,
            position.Y + Size + padding,
            position.Z + Size + padding
        );

        vertexCounts[0] = 0;
        vertexCounts[1] = 0;
    }

    public bool IsVisible(ICuller camera, Vector3D<double> viewPos, float renderDistance)
    {
        if (!camera.IsBoundingBoxInFrustum(BoundingBox)) return false;

        double dx = PositionPlus.X - viewPos.X;
        double dy = PositionPlus.Y - viewPos.Y;
        double dz = PositionPlus.Z - viewPos.Z;

        return (dx * dx + dz * dz) < (renderDistance * renderDistance) && Math.Abs(dy) < renderDistance;
    }

    public void UploadMeshData(PooledList<ChunkVertex>? solidMesh, PooledList<ChunkVertex>? translucentMesh)
    {
        vertexCounts[0] = 0;
        vertexCounts[1] = 0;

        if (solidMesh != null)
        {
            if (solidMesh.Count > 0)
            {
                Span<ChunkVertex> solidMeshData = solidMesh.Span;
                UploadMesh(0, solidMeshData);
            }

            solidMesh.Dispose();
        }

        if (translucentMesh != null)
        {
            if (translucentMesh.Count > 0)
            {
                Span<ChunkVertex> translucentMeshData = translucentMesh.Span;
                UploadMesh(1, translucentMeshData);
            }

            translucentMesh.Dispose();
        }
    }

    private unsafe void UploadMesh(int bufferIdx, Span<ChunkVertex> meshData)
    {
        vertexCounts[bufferIdx] = meshData.Length;

        // WebGPU path: create or replace the WgpuMesh.
        WebGpuDevice? wgpuDevice = WebGpuDevice.Current;
        if (wgpuDevice is not null)
        {
            _meshes[bufferIdx]?.Dispose();
            _meshes[bufferIdx] = WgpuMesh.FromChunkVertices(wgpuDevice, meshData);
            return;
        }

        // GL path: keep existing VertexBuffer/VertexArray.
        if (vertexBuffers[bufferIdx] == null)
        {
            vertexBuffers[bufferIdx] = new(meshData);
        }
        else
        {
            vertexBuffers[bufferIdx].BufferData(meshData);
        }

        if (vertexArrays[bufferIdx] == null)
        {
            vertexArrays[bufferIdx] = new();
            vertexArrays[bufferIdx].Bind();
            vertexBuffers[bufferIdx].Bind();

            // Inlined from the now-deleted ChunkVertexLayout.Bind().
            // Locations: 0=position(Sint16x4), 1=uv(Uint16x2), 2=color(Unorm8x4),
            // 3=light(Uint8x2), 4=arrayLayer(Uint8x2).
            const uint stride = 20;
            GLManager.GL.EnableVertexAttribArray(0);
            GLManager.GL.VertexAttribPointer(0, 4, GLEnum.Short, false, stride, (void*)0);
            GLManager.GL.EnableVertexAttribArray(1);
            GLManager.GL.VertexAttribIPointer(1, 2, GLEnum.UnsignedShort, stride, (void*)12);
            GLManager.GL.EnableVertexAttribArray(2);
            GLManager.GL.VertexAttribPointer(2, 4, GLEnum.UnsignedByte, true, stride, (void*)8);
            GLManager.GL.EnableVertexAttribArray(3);
            GLManager.GL.VertexAttribIPointer(3, 2, GLEnum.UnsignedByte, stride, (void*)16);
            GLManager.GL.EnableVertexAttribArray(4);
            GLManager.GL.VertexAttribIPointer(4, 1, GLEnum.UnsignedByte, stride, (void*)18);

            VertexArray.Unbind();
        }
    }

    public void Update(float deltaTime)
    {
        if (!HasFadedIn)
        {
            Age += deltaTime;
        }
    }

    public void Render(Shader shader, int pass, Vector3D<double> viewPos, Matrix4X4<float> modelViewMatrix)
    {
        if (disposed) return;

        if (pass < 0 || pass > 1)
            throw new ArgumentException("Pass must be 0 or 1");

        int vertexCount = vertexCounts[pass];

        if (vertexCount == 0)
            return;

        Vector3D<double> pos = new(PositionMinus.X - viewPos.X, PositionMinus.Y - viewPos.Y, PositionMinus.Z - viewPos.Z);
        pos += new Vector3D<double>(ClipPosition.X, ClipPosition.Y, ClipPosition.Z);

        modelViewMatrix = Matrix4X4.CreateTranslation(new Vector3D<float>((float)pos.X, (float)pos.Y, (float)pos.Z)) * modelViewMatrix;

        shader.SetUniformMatrix4("modelViewMatrix", modelViewMatrix);
        shader.SetUniform2("chunkPos", Position.X, Position.Z);

        vertexArrays[pass].Bind();

        GLManager.GL.DrawArrays(GLEnum.Triangles, 0, (uint)vertexCount);
    }

    /// <summary>
    ///     Draws the mesh for <paramref name="pass" /> through the WebGPU command encoder.
    ///     The caller has already bound the terrain pipeline and texture-array bind group,
    ///     and uploaded the per-chunk uniforms.
    /// </summary>
    public unsafe void RenderWebGpu(RenderPassEncoder* passEncoder, int pass)
    {
        if (disposed) return;
        if (pass < 0 || pass > 1) return;
        if (vertexCounts[pass] == 0) return;

        _meshes[pass]?.Draw(passEncoder);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        GC.SuppressFinalize(this);

        vertexBuffers[0]?.Dispose();
        vertexBuffers[1]?.Dispose();

        vertexArrays[0]?.Dispose();
        vertexArrays[1]?.Dispose();

        _meshes[0]?.Dispose();
        _meshes[1]?.Dispose();

        vertexCounts[0] = 0;
        vertexCounts[1] = 0;

        disposed = true;
    }
}
