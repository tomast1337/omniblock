using OmniBlock.Client.Rendering.Chunks.Occlusion;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Worlds.Core.Systems;
using Silk.NET.Maths;
using Silk.NET.WebGPU;

namespace OmniBlock.Client.Rendering.Chunks;

/// <summary>
///     One immutable, fully prepared presentation of a render section. Geometry is owned by four
///     independently replaceable horizontal pages, while visibility metadata and the page set are
///     published together as one section epoch.
/// </summary>
internal sealed class SectionPresentation : IDisposable
{
    private readonly SectionPagePresentation?[] _pages;
    private readonly TerrainGpuArenaSet? _gpuArenas;
    private readonly TerrainRenderRegionKey _regionKey;
    private bool _disposed;
    private MeshLifecycleRequest? _firstDrawTrace;

    private SectionPresentation(
        SectionPagePresentation?[] pages,
        int solidVertexCount,
        int translucentVertexCount,
        ChunkVisibilityStore visibilityData,
        bool isLit,
        long epoch,
        TerrainGpuArenaSet? gpuArenas,
        TerrainRenderRegionKey regionKey,
        MeshLifecycleDiagnostics? lifecycle,
        MeshLifecycleRequest? firstDrawTrace)
    {
        _pages = pages;
        SolidVertexCount = solidVertexCount;
        TranslucentVertexCount = translucentVertexCount;
        VisibilityData = visibilityData;
        IsLit = isLit;
        Epoch = epoch;
        _gpuArenas = gpuArenas;
        _regionKey = regionKey;
        Lifecycle = lifecycle;
        _firstDrawTrace = IsEmpty ? null : firstDrawTrace;
    }

    public int SolidVertexCount { get; }
    public int TranslucentVertexCount { get; }
    public ChunkVisibilityStore VisibilityData { get; }
    public bool IsLit { get; }
    public long Epoch { get; }
    public bool HasSolidGeometry => SolidVertexCount > 0;
    public bool HasTranslucentGeometry => TranslucentVertexCount > 0;
    public bool IsEmpty => !HasSolidGeometry && !HasTranslucentGeometry;
    public int SolidMeshSizeBytes => SolidVertexCount *
        (int)(WgpuMesh.ChunkVertexStride + WgpuMesh.ChunkLightVertexStride);
    public int TranslucentMeshSizeBytes => TranslucentVertexCount *
        (int)(WgpuMesh.ChunkVertexStride + WgpuMesh.ChunkLightVertexStride);
    internal MeshLifecycleDiagnostics? Lifecycle { get; }
    internal bool IsDisposed => _disposed;
    internal IReadOnlyList<SectionPagePresentation?> Pages => _pages;
    public long LightingEpoch => _pages.Length == 0
        ? -1
        : _pages.Max(static page => page?.LightingEpoch ?? -1);

    /// <summary>
    ///     Builds all replacement page resources before returning a candidate. Unselected pages
    ///     acquire shared immutable ownership from the current presentation. If anything fails,
    ///     every acquired/new page is released and the current presentation remains untouched.
    /// </summary>
    public static SectionPresentation Create(
        WebGpuDevice device,
        TerrainGpuArenaSet gpuArenas,
        Vector3D<int> sectionPosition,
        MeshPageBuildResult[] replacements,
        SectionPresentation? current,
        SectionMeshRebuildPlan rebuildPlan,
        ChunkVisibilityStore visibilityData,
        bool isLit,
        long epoch,
        MeshLifecycleDiagnostics? lifecycle,
        MeshLifecycleRequest? firstDrawTrace)
    {
        if (rebuildPlan.PageMask == 0) rebuildPlan = SectionMeshRebuildPlan.Full;
        if (current == null && !rebuildPlan.IsFull)
            throw new InvalidOperationException("An initial section presentation must build every mesh page.");
        if (replacements.Length != rebuildPlan.PageBuildCount)
            throw new ArgumentException("The replacement page count does not match its rebuild mask.", nameof(replacements));

        var regionKey = TerrainRenderRegionKey.FromSectionPosition(sectionPosition);
        var replacementByPage = new MeshPageBuildResult?[SectionMeshRebuildPlan.PageCount];
        foreach (var replacement in replacements)
        {
            if ((uint)replacement.Page >= SectionMeshRebuildPlan.PageCount ||
                replacementByPage[replacement.Page] != null ||
                !rebuildPlan.Includes(replacement.Page))
                throw new ArgumentException(
                    "Replacement mesh pages must be unique and selected by the rebuild mask.",
                    nameof(replacements));
            replacementByPage[replacement.Page] = replacement;
        }

        var pages = new SectionPagePresentation?[SectionMeshRebuildPlan.PageCount];
        try
        {
            for (var page = 0; page < pages.Length; page++)
            {
                pages[page] = rebuildPlan.Includes(page)
                    ? SectionPagePresentation.Create(
                        device, gpuArenas, regionKey, replacementByPage[page]!)
                    : current!._pages[page]?.Acquire();
            }

            var solidCount = pages.Sum(static page => page?.SolidVertexCount ?? 0);
            var translucentCount = pages.Sum(static page => page?.TranslucentVertexCount ?? 0);
            return new SectionPresentation(
                pages, solidCount, translucentCount, visibilityData, isLit, epoch,
                gpuArenas, regionKey, lifecycle, firstDrawTrace);
        }
        catch
        {
            foreach (var page in pages) page?.Release();
            throw;
        }
        finally
        {
            foreach (var replacement in replacements) replacement.Dispose();
        }
    }

    internal static SectionPresentation MetadataOnly(
        long epoch = -1,
        ChunkVisibilityStore visibilityData = default,
        bool isLit = false,
        int solidVertexCount = 0,
        int translucentVertexCount = 0) =>
        new([], solidVertexCount, translucentVertexCount, visibilityData, isLit, epoch,
            null, default, null, null);

    public SectionPresentationLightPlan CaptureLightingPlan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var pages = new SectionLightingPlan?[_pages.Length];
        for (var i = 0; i < pages.Length; i++)
            pages[i] = _pages[i]?.CaptureLightingPlan();
        return new SectionPresentationLightPlan(Epoch, pages);
    }

    public bool TryInstallLighting(
        WebGpuDevice device,
        in SectionPresentationLightEvaluation evaluation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_gpuArenas == null)
            throw new InvalidOperationException("A metadata-only presentation cannot install lighting.");
        if (evaluation.PresentationEpoch != Epoch || evaluation.Pages.Length != _pages.Length)
            return false;

        for (var i = 0; i < _pages.Length; i++)
        {
            if (evaluation.Pages[i] is { } pageEvaluation &&
                (_pages[i] == null || !_pages[i]!.OwnsLightingEpoch(pageEvaluation.SourceEpoch)))
                return false;
        }

        var replacements = new SectionLighting?[_pages.Length];
        try
        {
            for (var i = 0; i < _pages.Length; i++)
            {
                if (evaluation.Pages[i] is { } pageEvaluation)
                    replacements[i] = SectionLighting.CreateReplacementRegional(
                        device, _gpuArenas, _regionKey, pageEvaluation);
            }

            for (var i = 0; i < _pages.Length; i++)
            {
                if (replacements[i] is not { } replacement) continue;
                _pages[i]!.InstallLighting(replacement, evaluation.Pages[i]!.Value.SourceEpoch);
                replacements[i] = null;
            }
            return true;
        }
        finally
        {
            foreach (var replacement in replacements) replacement?.Dispose();
        }
    }

    public void RecordFirstDraw()
    {
        var trace = _firstDrawTrace;
        if (trace == null) return;
        _firstDrawTrace = null;
        Lifecycle?.Move(trace, MeshLifecycleStage.DrawRecorded);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var page in _pages) page?.Release();
    }
}

/// <summary>Reference-counted immutable geometry page shared across atomic section epochs.</summary>
internal sealed class SectionPagePresentation
{
    private SectionLighting? _lighting;
    private int _references = 1;

    private SectionPagePresentation(
        TerrainChunkQuadMesh? solid,
        TerrainChunkQuadMesh? translucent,
        SectionLighting? lighting,
        int solidVertexCount,
        int translucentVertexCount,
        ChunkDirectionalRanges solidRanges,
        ChunkDirectionalRanges translucentRanges)
    {
        Solid = solid;
        Translucent = translucent;
        _lighting = lighting;
        SolidVertexCount = solidVertexCount;
        TranslucentVertexCount = translucentVertexCount;
        SolidRanges = solidRanges;
        TranslucentRanges = translucentRanges;
    }

    public TerrainChunkQuadMesh? Solid { get; }
    public TerrainChunkQuadMesh? Translucent { get; }
    public int SolidVertexCount { get; }
    public int TranslucentVertexCount { get; }
    public ChunkDirectionalRanges SolidRanges { get; }
    public ChunkDirectionalRanges TranslucentRanges { get; }
    public long LightingEpoch => _lighting?.Epoch ?? -1;

    public static SectionPagePresentation Create(
        WebGpuDevice device,
        TerrainGpuArenaSet gpuArenas,
        TerrainRenderRegionKey regionKey,
        MeshPageBuildResult result)
    {
        TerrainChunkQuadMesh? solid = null;
        TerrainChunkQuadMesh? translucent = null;
        SectionLighting? lighting = null;
        var solidCount = result.Solid?.Count ?? 0;
        var translucentCount = result.Translucent?.Count ?? 0;
        try
        {
            if (solidCount != (result.SolidLighting?.VertexCount ?? 0) ||
                translucentCount != (result.TranslucentLighting?.VertexCount ?? 0))
                throw new ArgumentException("Terrain geometry and light models must have matching vertex counts.");
            if (solidCount != result.SolidRanges.AvailableQuadCount * 4 ||
                translucentCount != result.TranslucentRanges.AvailableQuadCount * 4)
                throw new ArgumentException("Terrain geometry and directional ranges must have matching vertex counts.");

            if (solidCount > 0)
                solid = TerrainChunkQuadMesh.Create(device, gpuArenas, regionKey, result.Solid!.Span);
            if (translucentCount > 0)
                translucent = TerrainChunkQuadMesh.Create(
                    device, gpuArenas, regionKey, result.Translucent!.Span);
            lighting = SectionLighting.CreateInitialRegional(
                device, gpuArenas, regionKey, result.SolidLighting, result.TranslucentLighting);
            return new SectionPagePresentation(
                solid, translucent, lighting, solidCount, translucentCount,
                result.SolidRanges, result.TranslucentRanges);
        }
        catch
        {
            solid?.Dispose();
            translucent?.Dispose();
            lighting?.Dispose();
            throw;
        }
    }

    public SectionPagePresentation Acquire()
    {
        while (true)
        {
            var references = Volatile.Read(ref _references);
            if (references <= 0)
                throw new ObjectDisposedException(nameof(SectionPagePresentation));
            if (Interlocked.CompareExchange(ref _references, references + 1, references) == references)
                return this;
        }
    }

    public SectionLightingPlan? CaptureLightingPlan() =>
        Volatile.Read(ref _lighting)?.CapturePlan();

    public bool OwnsLightingEpoch(long epoch) =>
        Volatile.Read(ref _lighting)?.Epoch == epoch;

    public void InstallLighting(SectionLighting replacement, long expectedEpoch)
    {
        var current = Volatile.Read(ref _lighting);
        if (current?.Epoch != expectedEpoch)
            throw new InvalidOperationException(
                $"Lighting epoch changed before publication; expected {expectedEpoch}, " +
                $"found {current?.Epoch.ToString() ?? "none"}.");
        Interlocked.Exchange(ref _lighting, replacement)?.Dispose();
    }

    public TerrainGpuBufferSlice LightSliceFor(int pass)
    {
        var lighting = _lighting;
        return lighting == null ? default : pass == 0 ? lighting.SolidSlice : lighting.TranslucentSlice;
    }

    public ChunkDirectionalRanges RangesFor(int pass) =>
        pass == 0 ? SolidRanges : TranslucentRanges;

    public void Release()
    {
        if (Interlocked.Decrement(ref _references) != 0) return;
        Solid?.Dispose();
        Translucent?.Dispose();
        Interlocked.Exchange(ref _lighting, null)?.Dispose();
    }
}

internal sealed record SectionPresentationLightPlan(
    long PresentationEpoch,
    SectionLightingPlan?[] Pages)
{
    public bool HasWork => Pages.Any(static page => page.HasValue);

    public SectionPresentationLightEvaluation Evaluate(ILightProvider lighting)
    {
        var pages = new SectionLightingEvaluation?[Pages.Length];
        for (var i = 0; i < pages.Length; i++)
            if (Pages[i] is { } page) pages[i] = page.Evaluate(lighting);
        return new SectionPresentationLightEvaluation(PresentationEpoch, pages);
    }
}

internal readonly record struct SectionPresentationLightEvaluation(
    long PresentationEpoch,
    SectionLightingEvaluation?[] Pages);
