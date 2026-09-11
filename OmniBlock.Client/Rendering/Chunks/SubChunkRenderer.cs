using OmniBlock.Client.Rendering.Chunks.Occlusion;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Util;
using OmniBlock.Util.Maths;
using Silk.NET.Maths;
using Silk.NET.WebGPU;

namespace OmniBlock.Client.Rendering.Chunks;

public class SubChunkRenderer : IDisposable
{
    public const int Size = 16;
    public const float FadeDuration = 1.0f;

    private readonly WgpuMesh?[] _meshes = new WgpuMesh?[2];

    private readonly int[] vertexCounts = new int[2];

    /// <summary>
    ///     Debug wireframe companion to the solid mesh (index 0 only — translucent geometry has no
    ///     wireframe view). Built alongside the normal upload, from the same triangle vertices, so
    ///     the debug toggle is instant with no remesh: each triangle's 3 edges become 6 line-list
    ///     vertices, reusing <see cref="ChunkVertex" /> as-is since <c>fs_wireframe</c> ignores every
    ///     field but position.
    /// </summary>
    private WgpuMesh? _wireframeMesh;
    internal MeshLifecycleDiagnostics? Lifecycle;
    internal MeshLifecycleRequest? FirstDrawTrace;

    public SubChunkRenderer? AdjacentDown;
    public SubChunkRenderer? AdjacentEast;
    public SubChunkRenderer? AdjacentNorth;
    public SubChunkRenderer? AdjacentSouth;
    public SubChunkRenderer? AdjacentUp;
    public SubChunkRenderer? AdjacentWest;
    private bool disposed;
    public ChunkDirectionMask IncomingDirections;
    public int LastVisibleFrame = -1;

    public ChunkVisibilityStore VisibilityData;

    public SubChunkRenderer(Vector3D<int> position)
    {
        Position = position;

        PositionPlus = new Vector3D<int>(position.X + Size / 2, position.Y + Size / 2, position.Z + Size / 2);
        ClipPosition = new Vector3D<int>(position.X & 1023, position.Y, position.Z & 1023);
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

    public bool HasTranslucentMesh => vertexCounts[1] > 0;
    public Vector3D<int> Position { get; }
    public Vector3D<int> PositionPlus { get; }
    public Vector3D<int> PositionMinus { get; }
    public Vector3D<int> ClipPosition { get; }
    public Box BoundingBox { get; }

    public float Age { get; private set; }
    public bool HasFadedIn => Age >= FadeDuration;

    public int SolidMeshSizeBytes => vertexCounts[0] * (int)WgpuMesh.ChunkVertexStride;
    public int TranslucentMeshSizeBytes => vertexCounts[1] * (int)WgpuMesh.ChunkVertexStride;

    public void Dispose()
    {
        if (disposed)
            return;

        GC.SuppressFinalize(this);

        _meshes[0]?.Dispose();
        _meshes[1]?.Dispose();
        _wireframeMesh?.Dispose();

        vertexCounts[0] = 0;
        vertexCounts[1] = 0;

        disposed = true;
    }

    public bool IsVisible(ICuller camera, Vector3D<double> viewPos, float renderDistance)
    {
        if (!camera.IsBoundingBoxInFrustum(BoundingBox)) return false;

        return IsWithinRenderDistance(viewPos, renderDistance);
    }

    internal bool IsWithinRenderDistance(Vector3D<double> viewPos, float renderDistance)
    {
        var dx = PositionPlus.X - viewPos.X;
        var dy = PositionPlus.Y - viewPos.Y;
        var dz = PositionPlus.Z - viewPos.Z;

        return dx * dx + dz * dz < renderDistance * renderDistance && Math.Abs(dy) < renderDistance;
    }

    public void UploadMeshData(PooledList<ChunkVertex>? solidMesh, PooledList<ChunkVertex>? translucentMesh)
    {
        vertexCounts[0] = 0;
        vertexCounts[1] = 0;

        if (solidMesh != null)
        {
            if (solidMesh.Count > 0)
            {
                var solidMeshData = solidMesh.Span;
                UploadMesh(0, solidMeshData);
            }

            solidMesh.Dispose();
        }

        if (translucentMesh != null)
        {
            if (translucentMesh.Count > 0)
            {
                var translucentMeshData = translucentMesh.Span;
                UploadMesh(1, translucentMeshData);
            }

            translucentMesh.Dispose();
        }
    }

    private void UploadMesh(int bufferIdx, Span<ChunkVertex> meshData)
    {
        vertexCounts[bufferIdx] = meshData.Length;

        _meshes[bufferIdx]?.Dispose();
        _meshes[bufferIdx] = WgpuMesh.FromChunkQuads(WebGpuDevice.Current!, meshData);

        if (bufferIdx == 0)
        {
            _wireframeMesh?.Dispose();
            _wireframeMesh = BuildWireframeMesh(meshData);
        }
    }

    /// <summary>
    ///     Expands four unique vertices per quad into the edges of its two indexed triangles.
    ///     Includes the diagonal, matching a triangle-level wireframe.
    /// </summary>
    private static WgpuMesh? BuildWireframeMesh(Span<ChunkVertex> quads)
    {
        if (quads.Length == 0) return null;
        if (quads.Length % 4 != 0)
        {
            throw new ArgumentException("Wireframe terrain input requires four vertices per quad.", nameof(quads));
        }

        var lines = new ChunkVertex[quads.Length * 3];
        var outIdx = 0;
        for (var i = 0; i < quads.Length; i += 4)
        {
            ChunkVertex a = quads[i], b = quads[i + 1], c = quads[i + 2], d = quads[i + 3];
            lines[outIdx++] = a;
            lines[outIdx++] = b;
            lines[outIdx++] = b;
            lines[outIdx++] = c;
            lines[outIdx++] = c;
            lines[outIdx++] = a;
            lines[outIdx++] = c;
            lines[outIdx++] = d;
            lines[outIdx++] = d;
            lines[outIdx++] = a;
            lines[outIdx++] = a;
            lines[outIdx++] = c;
        }

        return WgpuMesh.FromChunkVertices(WebGpuDevice.Current!, lines, PrimitiveTopology.LineList);
    }

    public void Update(float deltaTime)
    {
        if (!HasFadedIn)
        {
            Age += deltaTime;
        }
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

        if (_meshes[pass] is not { } mesh) return;
        mesh.Draw(passEncoder);
        RecordFirstDraw();
    }

    /// <summary>Draws the solid pass's wireframe companion — see <see cref="_wireframeMesh" />.</summary>
    public unsafe void RenderWireframeWebGpu(RenderPassEncoder* passEncoder)
    {
        if (disposed) return;
        if (vertexCounts[0] == 0) return;

        if (_wireframeMesh is not { } mesh) return;
        mesh.Draw(passEncoder);
        RecordFirstDraw();
    }

    private void RecordFirstDraw()
    {
        if (FirstDrawTrace == null) return;
        Lifecycle?.Move(FirstDrawTrace, MeshLifecycleStage.DrawRecorded);
        FirstDrawTrace = null;
    }
}
