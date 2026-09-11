using OmniBlock.Client.Rendering.Chunks.Occlusion;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Util.Maths;
using Silk.NET.Maths;
using Silk.NET.WebGPU;

namespace OmniBlock.Client.Rendering.Chunks;

public class SubChunkRenderer : IDisposable
{
    public const int Size = 16;
    public const float FadeDuration = 1.0f;

    private SectionPresentation? _presentation;

    public SubChunkRenderer? AdjacentDown;
    public SubChunkRenderer? AdjacentEast;
    public SubChunkRenderer? AdjacentNorth;
    public SubChunkRenderer? AdjacentSouth;
    public SubChunkRenderer? AdjacentUp;
    public SubChunkRenderer? AdjacentWest;
    private bool disposed;
    public ChunkDirectionMask IncomingDirections;
    public int LastVisibleFrame = -1;

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
    }

    public bool HasTranslucentMesh => _presentation?.HasTranslucentMesh == true;
    public Vector3D<int> Position { get; }
    public Vector3D<int> PositionPlus { get; }
    public Vector3D<int> PositionMinus { get; }
    public Vector3D<int> ClipPosition { get; }
    public Box BoundingBox { get; }

    public float Age { get; private set; }
    public bool HasFadedIn => Age >= FadeDuration;

    public int SolidMeshSizeBytes => _presentation?.SolidMeshSizeBytes ?? 0;
    public int TranslucentMeshSizeBytes => _presentation?.TranslucentMeshSizeBytes ?? 0;
    public ChunkVisibilityStore VisibilityData => _presentation?.VisibilityData ?? default;
    public long PresentedEpoch => _presentation?.Epoch ?? -1;
    public bool IsLit => _presentation?.IsLit == true;
    internal SectionPresentation? Presentation => _presentation;

    public void Dispose()
    {
        if (disposed)
            return;

        GC.SuppressFinalize(this);

        Interlocked.Exchange(ref _presentation, null)?.Dispose();

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

    /// <summary>
    ///     Publishes one already-complete presentation with a single reference exchange. The old
    ///     presentation remains authoritative until this point and its WebGPU buffers retire at a
    ///     later presentation boundary through <see cref="WgpuMesh.Dispose" />.
    /// </summary>
    internal void InstallPresentation(SectionPresentation presentation)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(presentation);
        if (_presentation != null && presentation.Epoch < _presentation.Epoch)
            throw new InvalidOperationException(
                $"Cannot replace section presentation epoch {_presentation.Epoch} with older epoch {presentation.Epoch}.");

        Interlocked.Exchange(ref _presentation, presentation)?.Dispose();
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
    public unsafe bool RenderWebGpu(RenderPassEncoder* passEncoder, int pass)
    {
        if (disposed) return false;
        if (pass < 0 || pass > 1) return false;
        var presentation = _presentation;
        var mesh = pass == 0 ? presentation?.Solid : presentation?.Translucent;
        if (mesh == null) return false;
        mesh.Draw(passEncoder, lightBuffer: presentation!.LightBufferFor(pass));
        presentation!.RecordFirstDraw();
        return true;
    }

    /// <summary>Draws the solid mesh through the device-wide quad wireframe indices.</summary>
    public unsafe bool RenderWireframeWebGpu(RenderPassEncoder* passEncoder)
    {
        if (disposed) return false;
        var presentation = _presentation;
        if (presentation?.Solid is not { } mesh) return false;
        mesh.DrawQuadWireframe(passEncoder, presentation.LightBufferFor(0));
        presentation.RecordFirstDraw();
        return true;
    }
}
