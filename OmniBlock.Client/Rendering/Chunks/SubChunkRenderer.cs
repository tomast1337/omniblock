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
    internal const float BoundsPadding = 6.0f;

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
    // Scratch state is generation-stamped by SectionVisibilityGraph and touched only by the
    // render thread. Storing it on the node removes per-edge hash-table traffic from culling.
    internal SectionVisibilityTraversalState VisibilityTraversal;

    public SubChunkRenderer(Vector3D<int> position)
    {
        Position = position;

        PositionPlus = new Vector3D<int>(position.X + Size / 2, position.Y + Size / 2, position.Z + Size / 2);
        ClipPosition = new Vector3D<int>(position.X & 1023, position.Y, position.Z & 1023);
        PositionMinus = position - ClipPosition;

        BoundingBox = new Box
        (
            position.X - BoundsPadding,
            position.Y - BoundsPadding,
            position.Z - BoundsPadding,
            position.X + Size + BoundsPadding,
            position.Y + Size + BoundsPadding,
            position.Z + Size + BoundsPadding
        );
    }

    public bool HasSolidGeometry => _presentation?.HasSolidGeometry == true;
    public bool HasTranslucentGeometry => _presentation?.HasTranslucentGeometry == true;
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
    /// <summary>
    ///     Stable group-2 storage record for this section's resident lifetime. It is deliberately
    ///     independent of the per-frame visible-list order so recorded regional commands can be
    ///     reused later.
    /// </summary>
    internal int DrawMetadataSlot { get; set; } = -1;

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
    ///     later presentation boundary through the regional arena retirement queue.
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
    internal unsafe DirectionalDrawStats RenderWebGpu(
        RenderPassEncoder* passEncoder,
        int pass,
        Vector3D<double> viewPosition,
        ref TerrainStreamBindingState binding,
        uint drawMetadataIndex)
    {
        if (disposed) return default;
        if (pass < 0 || pass > 1) return default;
        var presentation = _presentation;
        if (presentation == null) return default;

        var signature = 14695981039346656037UL;
        signature = AddSignature(signature, Position.X);
        signature = AddSignature(signature, Position.Y);
        signature = AddSignature(signature, Position.Z);
        signature = AddSignature(signature, presentation.Epoch);
        signature = AddSignature(signature, pass);
        var stats = new DirectionalDrawStats(0, 0, 0, 0, 0, signature);
        for (var pageIndex = 0; pageIndex < presentation.Pages.Count; pageIndex++)
        {
            var page = presentation.Pages[pageIndex];
            var mesh = pass == 0 ? page?.Solid : page?.Translucent;
            if (mesh == null) continue;

            var ranges = page!.RangesFor(pass);
            Span<ChunkQuadRange> selected = stackalloc ChunkQuadRange[7];
            var faceMask = DirectionalFaceVisibility.ForPage(Position, pageIndex, viewPosition);
            var selectedCount = ranges.Select(faceMask, selected);
            if (selectedCount == 0) continue;

            signature = AddSignature(signature, pageIndex);
            signature = AddSignature(signature, (int)faceMask);
            signature = AddSignature(signature, unchecked((long)mesh.CommandIdentity));
            signature = AddSignature(signature, DrawMetadataSlot);

            var streamChanged = mesh.BindChunkQuadStreams(passEncoder, ref binding);
            var submitted = 0;
            for (var i = 0; i < selectedCount; i++)
            {
                var range = selected[i];
                mesh.DrawBoundQuadRange(
                    passEncoder, (uint)range.FirstQuad, (uint)range.QuadCount,
                    firstInstance: drawMetadataIndex);
                submitted += range.QuadCount;
            }

            stats = stats.Add(
                ranges.AvailableQuadCount,
                submitted,
                selectedCount,
                ranges.UnassignedQuadCount,
                streamChanged ? 1 : 0,
                signature);
        }

        if (stats.DrawRanges > 0) presentation.RecordFirstDraw();
        return stats;
    }

    /// <summary>
    ///     Computes the opaque command identity and counters without recording WebGPU commands.
    ///     Bundle lookup uses this conservative signature; a mismatch always takes the ordinary
    ///     path, while a match may replay already-recorded commands.
    /// </summary>
    internal DirectionalDrawStats MeasureDirectionalDraw(int pass, Vector3D<double> viewPosition)
    {
        if (disposed || pass is < 0 or > 1 || _presentation is not { } presentation) return default;

        var signature = 14695981039346656037UL;
        signature = AddSignature(signature, Position.X);
        signature = AddSignature(signature, Position.Y);
        signature = AddSignature(signature, Position.Z);
        signature = AddSignature(signature, presentation.Epoch);
        signature = AddSignature(signature, pass);
        var stats = new DirectionalDrawStats(0, 0, 0, 0, 0, signature);
        for (var pageIndex = 0; pageIndex < presentation.Pages.Count; pageIndex++)
        {
            var page = presentation.Pages[pageIndex];
            var mesh = pass == 0 ? page?.Solid : page?.Translucent;
            if (mesh == null) continue;

            var ranges = page!.RangesFor(pass);
            Span<ChunkQuadRange> selected = stackalloc ChunkQuadRange[7];
            var faceMask = DirectionalFaceVisibility.ForPage(Position, pageIndex, viewPosition);
            var selectedCount = ranges.Select(faceMask, selected);
            if (selectedCount == 0) continue;

            signature = AddSignature(signature, pageIndex);
            signature = AddSignature(signature, (int)faceMask);
            signature = AddSignature(signature, unchecked((long)mesh.CommandIdentity));
            signature = AddSignature(signature, DrawMetadataSlot);
            var submitted = 0;
            for (var i = 0; i < selectedCount; i++) submitted += selected[i].QuadCount;
            stats = stats.Add(
                ranges.AvailableQuadCount,
                submitted,
                selectedCount,
                ranges.UnassignedQuadCount,
                0,
                signature);
        }

        return stats;
    }

    /// <summary>Records this section's selected solid ranges into an opaque render bundle.</summary>
    internal unsafe void RecordOpaqueBundle(
        RenderBundleEncoder* encoder,
        Vector3D<double> viewPosition,
        ref TerrainBundleStreamBindingState binding,
        uint drawMetadataIndex)
    {
        if (disposed || _presentation is not { } presentation) return;
        for (var pageIndex = 0; pageIndex < presentation.Pages.Count; pageIndex++)
        {
            var page = presentation.Pages[pageIndex];
            if (page?.Solid is not { } mesh) continue;

            var ranges = page.RangesFor(0);
            Span<ChunkQuadRange> selected = stackalloc ChunkQuadRange[7];
            var faceMask = DirectionalFaceVisibility.ForPage(Position, pageIndex, viewPosition);
            var selectedCount = ranges.Select(faceMask, selected);
            if (selectedCount == 0) continue;

            mesh.BindChunkQuadStreams(encoder, ref binding);
            for (var i = 0; i < selectedCount; i++)
            {
                var range = selected[i];
                mesh.DrawBoundQuadRange(
                    encoder, (uint)range.FirstQuad, (uint)range.QuadCount,
                    firstInstance: drawMetadataIndex);
            }
        }

        presentation.RecordFirstDraw();
    }

    private static ulong AddSignature(ulong value, long component)
    {
        value ^= unchecked((ulong)component);
        return value * 1099511628211UL;
    }

    /// <summary>Draws the solid mesh through the device-wide quad wireframe indices.</summary>
    internal unsafe int RenderWireframeWebGpu(
        RenderPassEncoder* passEncoder,
        ref TerrainStreamBindingState binding,
        out int streamBinds,
        uint drawMetadataIndex)
    {
        streamBinds = 0;
        if (disposed) return 0;
        var presentation = _presentation;
        if (presentation == null) return 0;

        var draws = 0;
        foreach (var page in presentation.Pages)
        {
            if (page?.Solid is not { } mesh) continue;
            if (mesh.DrawQuadWireframe(passEncoder, ref binding, drawMetadataIndex)) streamBinds++;
            draws++;
        }

        if (draws > 0) presentation.RecordFirstDraw();
        return draws;
    }
}

internal readonly record struct DirectionalDrawStats(
    int AvailableQuads,
    int SubmittedQuads,
    int DrawRanges,
    int UnassignedQuads,
    int StreamBinds,
    ulong CommandSignature = 0)
{
    public int RejectedQuads => AvailableQuads - SubmittedQuads;

    public DirectionalDrawStats Add(
        int availableQuads,
        int submittedQuads,
        int drawRanges,
        int unassignedQuads,
        int streamBinds,
        ulong commandSignature) =>
        new(
            AvailableQuads + availableQuads,
            SubmittedQuads + submittedQuads,
            DrawRanges + drawRanges,
            UnassignedQuads + unassignedQuads,
            StreamBinds + streamBinds,
            commandSignature);
}
