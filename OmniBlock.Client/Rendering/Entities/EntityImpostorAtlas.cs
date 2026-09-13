using System.Numerics;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Profiling;
using Silk.NET.WebGPU;
using CullMode = OmniBlock.Client.Rendering.Core.CullMode;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>
/// One provider's bounded-pose atlas with portable memory/disk caching. All GPU work is on the render thread;
/// candidates become sampleable only after every view of every bounded pose has been recorded.
/// Does not bake in a live entity/renderer or rewrite a buffer referenced by another draw.
/// </summary>
internal sealed unsafe class EntityImpostorAtlas : IDisposable
{
    private const int MaxInstances = 2048;
    private static readonly ILogger s_log = Log.Instance.For<EntityImpostorAtlas>();
    private readonly IEntityImpostorProvider _provider;
    private WebGpuDevice? _device;
    private WgpuTexture? _atlas;
    private Texture* _depth;
    private TextureView* _depthView;
    private WgpuMesh[]? _geometry;
    private WgpuPipeline? _capture, _captureDepth, _present;
    private WgpuStorageBuffer? _instances;
    private readonly Instance[] _staging = new Instance[MaxInstances];
    private int _count;
    private bool _requested, _failed;
    private long _generation = -1;
    private long _gpuErrors;
    private float _radius;
    private readonly EntityImpostorMemoryCache _memory;
    private CancellationTokenSource _cancel = new();
    private long _epoch;
    private string? _key, _cacheDirectory;
    private Texture2D.CaptureSource[]? _sources;
    private EntityImpostorCaptureLayer[]? _layers;
    private int _layerCount = 1;
    private bool _lookupComplete, _persistenceStarted;
    private Task<CacheWork>? _work;
    private Task<CacheWrite>? _write;
    private WgpuAtlasReadback? _readback;
    private long _lastPrepare;
    private long _workStarted, _workEpoch, _writeStarted, _writeEpoch;
    private long _requestStarted;
    private double _captureCpuMs;
    private bool _readyLatencyRecorded;
    private int _deferredFrames;
    private sealed record CacheWork(long Epoch, string Key, bool DiskLookup, EntityImpostorCache.Atlas? Atlas, string? Error);
    private sealed record CacheWrite(long Epoch, bool Saved, string? Error);
    public int MemoryHits { get; private set; }
    public int DiskHits { get; private set; }
    public int CacheMisses { get; private set; }
    public int CacheWrites { get; private set; }
    public int CacheErrors { get; private set; }
    public int Cancellations { get; private set; }
    public int StaleResults { get; private set; }
    public int CapturedViews { get; private set; }
    public int Invalidations { get; private set; }
    public double BakeQueueAgeMs => _requested && !Ready && _requestStarted != 0
        ? Stopwatch.GetElapsedTime(_requestStarted).TotalMilliseconds : 0;
    public double LastBakeLatencyMs { get; private set; }
    public double TotalBakeLatencyMs { get; private set; }
    public int CompletedBakes { get; private set; }
    public double CaptureCpuMs => _captureCpuMs;
    /// <summary>Known atlas/depth, geometry, and instance-buffer bytes; excludes driver/pipeline overhead.</summary>
    public long ResidentGpuBytes
    {
        get
        {
            var pixels = _atlas == null ? 0L : (long)EntityImpostorLayout.Width *
                EntityImpostorLayout.AtlasHeight(_layerCount);
            var geometry = _geometry == null || _layers == null ? 0L :
                _layers.SelectMany(layer => layer.Poses).Sum(pose => (long)pose.Length * 32);
            return pixels * 8 + geometry + (_instances == null ? 0 : MaxInstances * 80L);
        }
    }
    /// <summary>Managed source, geometry, and fixed instance-staging bytes retained by this atlas.</summary>
    public long StagingBytes => (_sources?.Sum(source => (long)source.Pixels.Length) ?? 0) +
        (_layers?.SelectMany(layer => layer.Poses).Sum(pose => (long)pose.Length * 32) ?? 0) +
        MaxInstances * 80L;
    public long MemoryBytes => _memory.Bytes;
    public bool ReadbackPending => _readback != null;
    public bool CapturePending => Enabled && _requested && !Ready && !_failed;
    internal bool HoldReadbackForTest { get; set; }
    internal bool HoldCaptureForTest { get; set; }
    public bool Enabled { get; set; }
    public int CompletedViews { get; private set; }
    public bool Ready => _atlas != null && MayPublish(CompletedViews, _layerCount, _failed);
    public int LastSubmitted { get; private set; }
    public int Failures { get; private set; }
    public int Replacements { get; private set; }
    public int PendingFallbacks { get; private set; }
    public int LastPoseMask { get; private set; }
    public int LastHurtSubmitted { get; private set; }
    public int LastOverlaySubmitted { get; private set; }
    public int LastDrawBatches { get; private set; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Instance { public Vector4 Center, Right, Up, UV, Effects; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Uniforms { public Matrix4x4 View, Projection; public Vector4 FogColor, Fog; }

    public EntityImpostorAtlas(IEntityImpostorProvider provider, EntityImpostorMemoryCache memory)
    {
        _provider = provider;
        _memory = memory;
    }

    // GPU-free state gates are also tested without a graphics device.
    internal static bool MayPublish(int completed, int layers, bool failed) =>
        completed == EntityImpostorLayout.CapturesFor(layers) && !failed;
    internal static int ViewsThisFrame(int completed, int layers = 1) =>
        Math.Clamp(EntityImpostorLayout.CapturesFor(layers) - completed, 0, 2);

    public void Reset()
    {
        if (_requested || _atlas != null || _work != null || _write != null || _readback != null) Invalidations++;
        _epoch++;
        if (!_cancel.IsCancellationRequested && (_work is { IsCompleted: false } || _write is { IsCompleted: false } || _readback != null || (_atlas != null && !Ready))) Cancellations++;
        _cancel.Cancel(); _cancel.Dispose(); _cancel = new CancellationTokenSource();
        _readback?.Dispose(); _readback = null;
        _key = null; _sources = null; _layers = null; _layerCount = 1;
        _lookupComplete = _persistenceStarted = false;
        if (_atlas != null) Replacements++;
        _atlas?.Dispose(); _atlas = null;
        if (_geometry != null) foreach (var geometry in _geometry) geometry.Dispose();
        _geometry = null;
        if (_device != null) WgpuRelease.Deferred(_device, [], 0, (nint)_depthView, (nint)_depth);
        _depthView = null; _depth = null;
        CompletedViews = 0; _requested = _failed = false; _count = LastSubmitted = LastDrawBatches = 0;
        _requestStarted = 0; _readyLatencyRecorded = false;
    }

    public void Dispose()
    {
        Reset();
        var instances = _instances; _instances = null;
        // Pipelines own bind-group layouts; retire after buffers/textures and recorded draws.
        var capture = _capture; var captureDepth = _captureDepth; var present = _present;
        if (_device != null) _device.Retire(() =>
        { instances?.Dispose(); capture?.Dispose(); captureDepth?.Dispose(); present?.Dispose(); });
        _capture = _captureDepth = _present = null;
    }

    /// <summary>Before any world render pass, on the frame's command encoder.</summary>
    public void Prepare(WebGpuDevice device, CommandEncoder* encoder, TextureManager textures,
        string gameDataDirectory, bool allowCapture)
    {
        if (_device != null && !ReferenceEquals(_device, device)) Dispose();
        _device = device;
        _cacheDirectory ??= Path.Combine(gameDataDirectory, "cache", "entity-impostors", "v3");
        if (_generation != textures.ResourceGeneration || (!Enabled && (_requested || _atlas != null)))
        {
            Reset(); _generation = textures.ResourceGeneration;
        }
        _count = LastSubmitted = LastDrawBatches = PendingFallbacks = LastPoseMask = LastHurtSubmitted = LastOverlaySubmitted = 0;
        _capture?.ResetUniformPool(); _captureDepth?.ResetUniformPool(); _present?.ResetUniformPool();
        // Compare the effective upload snapshot as well as the pack token. This catches in-place
        // texture/sampler replacement without trusting a pack display name or mutable file path.
        var resolved = Enabled && _requested
            ? _provider.TexturePaths.Select(path => textures.GetTextureId(path).Texture).ToArray()
            : [];
        var sources = resolved.Select(texture => texture?.ImpostorSource).ToArray();
        if (_sources != null && (_sources.Length != sources.Length ||
            _sources.Where((source, index) => !ReferenceEquals(source, sources[index])).Any()))
        { Reset(); _requested = Enabled; }
        try { PollCacheWork(device); PollReadback(); RecordReadyLatency(); }
        catch (Exception ex) { Dispose(); _failed = true; Failures++; s_log.LogError(ex, "Impostor cache installation failed; keeping 3D."); }
        if (_atlas != null && device.ErrorCount != _gpuErrors)
        {
            Dispose(); _failed = true; Failures++;
            s_log.LogError("GPU error during {Provider} impostor lifetime; falling back to 3D.", _provider.Id);
        }
        var now = Stopwatch.GetTimestamp();
        var frameMs = _lastPrepare == 0 ? 0 : Stopwatch.GetElapsedTime(_lastPrepare, now).TotalMilliseconds;
        _lastPrepare = now;
        if (!Enabled || !_requested || _failed || sources.Length is < 1 or > 2 || sources.Any(source => source == null)) return;
        if (Ready) { StartReadback(device, encoder); return; }
        if (!allowCapture) return;
        using var captureTiming = Profiler.Begin("EntityImpostorCaptureCpu");
        try
        {
            if (_sources == null && _work == null)
            {
                _layers = _provider.BuildLayers();
                if (_layers.Length != sources.Length || _layers.Where((layer, index) =>
                    layer.TexturePath != _provider.TexturePaths[index] || layer.Poses.Length != EntityImpostorLayout.Poses).Any())
                    throw new InvalidDataException($"Provider '{_provider.Id}' returned an invalid layer layout.");
                _layerCount = _layers.Length;
                _sources = sources.Select(source => source!).ToArray();
                _radius = _layers.SelectMany(layer => layer.Poses).SelectMany(pose => pose)
                    .Max(vertex => vertex.Position.Length()) * 1.03f;
                var layers = _layers; var capturedSources = _sources; var radius = _radius;
                var epoch = _epoch; var cancel = _cancel.Token;
                _workStarted = Stopwatch.GetTimestamp(); _workEpoch = epoch;
                _work = Task.Run(() =>
                {
                    try { cancel.ThrowIfCancellationRequested(); return new CacheWork(epoch,
                        EntityImpostorCache.Key(_provider.CacheIdentity, layers, capturedSources, radius), false, null, null); }
                    catch (OperationCanceledException) { return new CacheWork(epoch, "", false, null, null); }
                    catch (Exception ex) { return new CacheWork(epoch, "", false, null, ex.Message); }
                });
                return;
            }
            if (!_lookupComplete) return;
            if (HoldCaptureForTest && CompletedViews >= 2) return;
            // Soft scheduling budget, with eventual service under continuous frame pressure.
            if (frameMs > 25 && ++_deferredFrames < 8) return;
            _deferredFrames = 0;
            var captureStarted = Stopwatch.GetTimestamp();
            EnsureResources(device);
            if (device.ErrorCount != _gpuErrors) throw new InvalidOperationException("GPU rejected the impostor resources.");
            var skins = resolved.Select(texture => texture?.Wgpu).ToArray();
            if (skins.Any(skin => skin == null)) return;
            var views = ViewsThisFrame(CompletedViews, _layerCount);
            for (var n = 0; n < views; n++)
            {
                var index = CompletedViews;
                var layer = index / EntityImpostorLayout.Captures;
                var capture = index % EntityImpostorLayout.Captures;
                var pose = capture / EntityImpostorLayout.Views;
                var view = capture % EntityImpostorLayout.Views;
                RenderPassColorAttachment color = new() { View = _atlas!.View, LoadOp = index == 0 ? LoadOp.Clear : LoadOp.Load,
                    StoreOp = StoreOp.Store, ClearValue = default, DepthSlice = uint.MaxValue };
                RenderPassDepthStencilAttachment depth = new() { View = _depthView, DepthLoadOp = LoadOp.Clear,
                    DepthStoreOp = StoreOp.Store, DepthClearValue = 1, StencilLoadOp = LoadOp.Undefined, StencilStoreOp = StoreOp.Undefined };
                RenderPassDescriptor desc = new() { ColorAttachmentCount = 1, ColorAttachments = &color, DepthStencilAttachment = &depth };
                var pass = device.Api.CommandEncoderBeginRenderPass(encoder, in desc);
                try
                {
                    var x = (uint)(view % EntityImpostorLayout.Columns * EntityImpostorLayout.Cell + EntityImpostorLayout.Padding);
                    var y = (uint)(layer * EntityImpostorLayout.Height +
                        (pose * EntityImpostorLayout.RowsPerPose + view / EntityImpostorLayout.Columns) *
                        EntityImpostorLayout.Cell + EntityImpostorLayout.Padding);
                    device.Api.RenderPassEncoderSetViewport(pass, x, y, EntityImpostorLayout.Tile, EntityImpostorLayout.Tile, 0, 1);
                    device.Api.RenderPassEncoderSetScissorRect(pass, x, y, EntityImpostorLayout.Tile, EntityImpostorLayout.Tile);
                    var direction = EntityLodDirections.Get(view);
                    var (_, up) = EntityImpostorLayout.Basis(direction);
                    var matrix = Matrix4x4.CreateLookAt(direction * (_radius * 3), Vector3.Zero, up) *
                        Matrix4x4.CreateOrthographic(_radius * 2, _radius * 2, _radius, _radius * 5);
                    if (layer > 0)
                    {
                        // Preserve the live renderer's layer visibility: body geometry seeds depth
                        // without color, then only fleece pixels actually in front enter the mask.
                        _captureDepth!.Bind(pass);
                        _captureDepth.BindNextUniforms(pass, matrix);
                        WgpuPipeline.BindGroup(pass, 1,
                            skins[0]!.BindGroupFor(_captureDepth.TextureBindGroupLayout), device.Api);
                        _geometry![pose].Draw(pass);
                    }
                    _capture!.Bind(pass);
                    _capture.BindNextUniforms(pass, matrix);
                    WgpuPipeline.BindGroup(pass, 1, skins[layer]!.BindGroupFor(_capture.TextureBindGroupLayout), device.Api);
                    _geometry![layer * EntityImpostorLayout.Poses + pose].Draw(pass);
                }
                finally { device.Api.RenderPassEncoderEnd(pass); device.Api.RenderPassEncoderRelease(pass); }
                CompletedViews++;
                CapturedViews++;
                if (Stopwatch.GetElapsedTime(captureStarted).TotalMilliseconds >= .5) break;
            }
            _captureCpuMs += Stopwatch.GetElapsedTime(captureStarted).TotalMilliseconds;
            RecordReadyLatency();
            if (Ready) StartReadback(device, encoder);
        }
        catch (Exception ex)
        {
            Dispose(); _failed = true; Failures++;
            s_log.LogError(ex, "{Provider} impostor capture failed; keeping 3D until reset/resource reload.", _provider.Id);
        }
    }

    private void ReportCacheError(string? message)
    {
        if (message == null) return;
        CacheErrors++;
        if (CacheErrors <= 3) s_log.LogWarning("Impostor cache unavailable/corrupt; using GPU capture or 3D: {Reason}", message);
    }

    private void PollCacheWork(WebGpuDevice device)
    {
        if ((_work is { IsCompleted: false } && _workEpoch == _epoch && Stopwatch.GetElapsedTime(_workStarted).TotalSeconds > 5) ||
            (_write is { IsCompleted: false } && _writeEpoch == _epoch && Stopwatch.GetElapsedTime(_writeStarted).TotalSeconds > 5))
        {
            // Retain timed-out task slots until they settle: repeated reloads cannot fan out an
            // unbounded set of blocked file-system jobs. A known-key lookup timeout can bake;
            // a hash timeout cannot safely name a cache entry and conservatively retains 3D.
            _cancel.Cancel(); _cancel.Dispose(); _cancel = new CancellationTokenSource(); _epoch++; Cancellations++;
            if (_key != null) _lookupComplete = true; else _failed = true;
            ReportCacheError("Cache worker exceeded its five-second deadline.");
        }
        if (_write is { IsCompleted: true })
        {
            var result = _write.Result; _write = null;
            if (result.Epoch != _epoch) StaleResults++;
            else { if (result.Saved) CacheWrites++; ReportCacheError(result.Error); }
        }
        if (_work is not { IsCompleted: true }) return;
        var work = _work.Result; _work = null;
        if (work.Epoch != _epoch) { StaleResults++; return; }
        ReportCacheError(work.Error);
        if (string.IsNullOrEmpty(work.Key)) { _failed = true; return; }
        _key = work.Key;
        if (!work.DiskLookup)
        {
            if (_memory.Get(work.Key) is { } cached) { MemoryHits++; InstallCached(device, cached); return; }
            var directory = _cacheDirectory!; var epoch = _epoch; var radius = _radius;
            var layerCount = _layerCount; var cancel = _cancel.Token;
            _workStarted = Stopwatch.GetTimestamp(); _workEpoch = epoch;
            _work = Task.Run(() =>
            {
                try { return new CacheWork(epoch, work.Key, true,
                    EntityImpostorCache.Read(directory, work.Key, radius, layerCount, cancel), null); }
                catch (OperationCanceledException) { return new CacheWork(epoch, "", true, null, null); }
                catch (Exception ex) { return new CacheWork(epoch, work.Key, true, null, ex.Message); }
            });
        }
        else if (work.Atlas is { } atlas) { DiskHits++; _memory.Put(atlas); InstallCached(device, atlas); }
        else { CacheMisses++; _lookupComplete = true; }
    }

    private void InstallCached(WebGpuDevice device, EntityImpostorCache.Atlas atlas)
    {
        // Only current-epoch jobs reach this call. Validate before GPU allocation, then publish
        // after the complete single-level upload has been queued ahead of this frame's draws.
        _layerCount = atlas.Layers;
        EnsureResources(device, false);
        _atlas = CreateAtlas(device);
        _atlas.WriteLevel(0, 0, 0, EntityImpostorLayout.Width,
            (uint)EntityImpostorLayout.AtlasHeight(_layerCount), atlas.Pixels);
        _radius = atlas.Radius;
        CompletedViews = EntityImpostorLayout.CapturesFor(_layerCount);
        _persistenceStarted = true;
    }

    private void StartReadback(WebGpuDevice device, CommandEncoder* encoder)
    {
        if (_persistenceStarted || _write != null || _readback != null || _key == null) return;
        _persistenceStarted = true;
        _readback = new WgpuAtlasReadback(device, encoder, _atlas!);
    }

    /// <summary>Called after QueueSubmit, never before recording finishes.</summary>
    public void AfterSubmit(WebGpuDevice device)
    {
        if (_device != null && !ReferenceEquals(_device, device)) { Dispose(); _device = device; _generation = -1; }
        _readback?.AfterSubmit();
        if (Enabled || WgpuAtlasReadback.PendingCallbacks != 0) device.PollNonBlocking();
    }

    private void PollReadback()
    {
        if (HoldReadbackForTest) return;
        if (_readback == null || !_readback.TryComplete(out var pixels)) return;
        _readback = null;
        if (pixels == null || _key == null) { ReportCacheError("Atlas readback failed or timed out."); return; }
        var atlas = new EntityImpostorCache.Atlas(_key, _radius, _layerCount, pixels);
        _memory.Put(atlas);
        var directory = _cacheDirectory!; var epoch = _epoch; var cancel = _cancel.Token;
        _writeStarted = Stopwatch.GetTimestamp(); _writeEpoch = epoch;
        _write = Task.Run(() =>
        {
            try { EntityImpostorCache.Write(directory, atlas, cancel); return new CacheWrite(epoch, true, null); }
            catch (OperationCanceledException) { return new CacheWrite(epoch, false, null); }
            catch (Exception ex) { return new CacheWrite(epoch, false, ex.Message); }
        });
    }

    public bool TrySubmit(EntityLodSelector.Decision decision,
        Vector3 cameraRelativePosition, float yaw, float light, int pose, bool hurt, Vector4 layerEffects)
    {
        if (!Enabled || decision.Intended != EntityLodTier.Impostor ||
            decision.ViewIndex < 0 || pose is < 0 or >= EntityImpostorLayout.Poses) return false;
        _requested = true;
        if (_requestStarted == 0) _requestStarted = Stopwatch.GetTimestamp();
        if (!Ready || _count == MaxInstances) { PendingFallbacks++; return false; }
        var rotation = Matrix4x4.CreateRotationY(-yaw * MathF.PI / 180);
        var (right, up) = EntityImpostorLayout.Basis(EntityLodDirections.Get(decision.ViewIndex));
        _staging[_count++] = new Instance { Center = new Vector4(cameraRelativePosition, light),
            Right = new Vector4(Vector3.TransformNormal(right, rotation) * _radius, hurt ? 0.4f : 0),
            Up = new Vector4(Vector3.TransformNormal(up, rotation) * _radius, _layerCount > 1 ? 1f / _layerCount : 0),
            UV = EntityImpostorLayout.UV(decision.ViewIndex, pose, _layerCount), Effects = layerEffects };
        LastPoseMask |= 1 << pose;
        if (hurt) LastHurtSubmitted++;
        if (_layerCount > 1 && layerEffects.W > 0) LastOverlaySubmitted++;
        return true;
    }

    public void Draw()
    {
        if (_count == 0 || !Ready || RenderSystem.DrawTarget is not WebGpuDrawTarget target) return;
        var started = EntityPresentationMetrics.StartTimer();
        var fog = RenderSystem.Fog;
        _instances!.Write<Instance>(_staging.AsSpan(0, _count));
        EntityPresentationMetrics.Uploaded(_count * 80);
        var pass = target.CurrentPass;
        _present!.Bind(pass);
        _present.BindNextUniforms(pass, new Uniforms {
            View = WebGpuDrawTarget.ToNumerics(RenderSystem.ModelView.Top),
            Projection = WebGpuDrawTarget.ToNumerics(WgpuClip.FromGl(RenderSystem.Projection.Top)),
            FogColor = new Vector4(fog.Color.X, fog.Color.Y, fog.Color.Z, fog.Color.W),
            Fog = new Vector4(fog.Start, fog.End, fog.Density, RenderSystem.FogEnabled ? (int)fog.Curve + 1 : 0) });
        _instances.Bind(pass);
        WgpuPipeline.BindGroup(pass, 2, _atlas!.BindGroupFor(_present.TextureArrayBindGroupLayout), _device!.Api);
        _device.Api.RenderPassEncoderDraw(pass, 6, (uint)_count, 0, 0);
        EntityPresentationMetrics.Drew(_count);
        EntityPresentationMetrics.SubmitFinished(started);
        LastSubmitted = _count;
        LastDrawBatches = 1;
        _count = 0;
    }

    private void RecordReadyLatency()
    {
        if (!Ready || _readyLatencyRecorded || _requestStarted == 0) return;
        _readyLatencyRecorded = true;
        LastBakeLatencyMs = Stopwatch.GetElapsedTime(_requestStarted).TotalMilliseconds;
        TotalBakeLatencyMs += LastBakeLatencyMs;
        CompletedBakes++;
    }

    private void EnsureResources(WebGpuDevice device, bool capture = true)
    {
        _gpuErrors = device.ErrorCount;
        if (_capture == null)
        {
            BindGroupLayoutEntry[] uniforms = [new() { Binding = 0, Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                Buffer = new BufferBindingLayout { Type = BufferBindingType.Uniform, MinBindingSize = 64 } }];
            BindGroupLayoutEntry[] textures = [new() { Binding = 0, Visibility = ShaderStage.Fragment,
                Texture = new TextureBindingLayout { SampleType = TextureSampleType.Float, ViewDimension = TextureViewDimension.Dimension2D } },
                new() { Binding = 1, Visibility = ShaderStage.Fragment, Sampler = new SamplerBindingLayout { Type = SamplerBindingType.Filtering } }];
            var attributes = stackalloc VertexAttribute[3];
            attributes[0] = new() { ShaderLocation = 0, Format = VertexFormat.Float32x3, Offset = 0 };
            attributes[1] = new() { ShaderLocation = 1, Format = VertexFormat.Float32x2, Offset = 12 };
            attributes[2] = new() { ShaderLocation = 2, Format = VertexFormat.Float32x3, Offset = 20 };
            VertexBufferLayout layout = new() { ArrayStride = 32, StepMode = VertexStepMode.Vertex, AttributeCount = 3, Attributes = attributes };
            var state = RenderState.Opaque with { Cull = CullMode.None };
            _capture = new WgpuPipeline(device, EntityImpostorShaders.Capture, "vs_main", 64, uniforms, textures,
                &layout, 1, state, WgpuTexture.Format, WgpuFramebuffer.DepthFormat, label: $"{_provider.Id} impostor capture");
            _captureDepth = new WgpuPipeline(device, EntityImpostorShaders.Capture, "vs_main", 64, uniforms, textures,
                &layout, 1, state with { ColorWrite = false }, WgpuTexture.Format, WgpuFramebuffer.DepthFormat,
                label: $"{_provider.Id} impostor layer depth");
            uniforms[0].Buffer.MinBindingSize = 160;
            BindGroupLayoutEntry[] storage = [new() { Binding = 0, Visibility = ShaderStage.Vertex,
                Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage } }];
            _present = new WgpuPipeline(device, EntityImpostorShaders.Present, "vs_main", 160, uniforms, storage,
                null, 0, state, device.SurfaceFormat, WgpuFramebuffer.DepthFormat, textureArrayEntries: textures,
                label: $"{_provider.Id} impostor cutout");
            _instances = new WgpuStorageBuffer(device, MaxInstances * 80, _present.TextureBindGroupLayout);
        }
        if (_atlas != null || !capture) return;
        _geometry = _layers!.SelectMany(layer => layer.Poses)
            .Select(pose => new WgpuMesh(device, MemoryMarshal.AsBytes(pose.AsSpan()), 32)).ToArray();
        // Single mip + nearest sampling for this prototype: padded tiles cannot bleed into each
        // other. Alpha-aware mip generation and filtering are required before general rollout.
        _atlas = CreateAtlas(device);
        TextureDescriptor depth = new() { Usage = TextureUsage.RenderAttachment, Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D(EntityImpostorLayout.Width, (uint)EntityImpostorLayout.AtlasHeight(_layerCount), 1),
            Format = WgpuFramebuffer.DepthFormat, MipLevelCount = 1, SampleCount = 1 };
        _depth = device.Api.DeviceCreateTexture(device.Device, in depth);
        _depthView = device.Api.TextureCreateView(_depth, null);
    }

    private WgpuTexture CreateAtlas(WebGpuDevice device) => new(device, EntityImpostorLayout.Width,
        (uint)EntityImpostorLayout.AtlasHeight(_layerCount), 1,
        WgpuSamplerDescription.Nearest with { AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge },
        TextureUsage.RenderAttachment | TextureUsage.CopySrc);
}
