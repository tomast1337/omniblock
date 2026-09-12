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
/// Opt-in, one-model/bounded-pose prototype with portable memory/disk caching. All GPU work is on the render thread;
/// candidates become sampleable only after every view of every bounded pose has been recorded.
/// Does not bake in a live entity/renderer or rewrite a buffer referenced by another draw.
/// </summary>
internal sealed unsafe class EntityImpostorPrototype : IDisposable
{
    private const int MaxInstances = 2048;
    private static readonly ILogger s_log = Log.Instance.For<EntityImpostorPrototype>();
    private WebGpuDevice? _device;
    private WgpuTexture? _atlas;
    private Texture* _depth;
    private TextureView* _depthView;
    private WgpuMesh[]? _geometry;
    private WgpuPipeline? _capture, _present;
    private WgpuStorageBuffer? _instances;
    private readonly Instance[] _staging = new Instance[MaxInstances];
    private int _count;
    private bool _requested, _failed;
    private long _generation = -1;
    private long _gpuErrors;
    private float _radius;
    private readonly EntityImpostorMemoryCache _memory = new();
    private CancellationTokenSource _cancel = new();
    private long _epoch;
    private string? _key, _cacheDirectory;
    private Texture2D.CaptureSource? _source;
    private CowImpostorGeometry.Vertex[][]? _vertices;
    private bool _lookupComplete, _persistenceStarted;
    private Task<CacheWork>? _work;
    private Task<CacheWrite>? _write;
    private WgpuAtlasReadback? _readback;
    private long _lastPrepare;
    private long _workStarted, _workEpoch, _writeStarted, _writeEpoch;
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
    public long MemoryBytes => _memory.Bytes;
    public bool ReadbackPending => _readback != null;
    internal bool HoldReadbackForTest { get; set; }
    internal bool HoldCaptureForTest { get; set; }
    public bool Enabled { get; set; }
    public bool ForceTierForTest { get; set; }
    public int CompletedViews { get; private set; }
    public bool Ready => _atlas != null && MayPublish(CompletedViews, _failed);
    public int LastSubmitted { get; private set; }
    public int Failures { get; private set; }
    public int Replacements { get; private set; }
    public int PendingFallbacks { get; private set; }
    public int LastPoseMask { get; private set; }
    public int LastHurtSubmitted { get; private set; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Instance { public Vector4 Center, Right, Up, UV, Effects; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Uniforms { public Matrix4x4 View, Projection; public Vector4 FogColor, Fog; }

    // GPU-free state gates are also tested without a graphics device.
    internal static bool MayPublish(int completed, bool failed) => completed == EntityImpostorLayout.Captures && !failed;
    internal static int ViewsThisFrame(int completed) => Math.Clamp(EntityImpostorLayout.Captures - completed, 0, 2);

    public void Reset()
    {
        _epoch++;
        if (!_cancel.IsCancellationRequested && (_work is { IsCompleted: false } || _write is { IsCompleted: false } || _readback != null || (_atlas != null && !Ready))) Cancellations++;
        _cancel.Cancel(); _cancel.Dispose(); _cancel = new CancellationTokenSource();
        _readback?.Dispose(); _readback = null;
        _key = null; _source = null; _vertices = null; _lookupComplete = _persistenceStarted = false;
        if (_atlas != null) Replacements++;
        _atlas?.Dispose(); _atlas = null;
        if (_geometry != null) foreach (var geometry in _geometry) geometry.Dispose();
        _geometry = null;
        if (_device != null) WgpuRelease.Deferred(_device, [], 0, (nint)_depthView, (nint)_depth);
        _depthView = null; _depth = null;
        CompletedViews = 0; _requested = _failed = false; _count = LastSubmitted = 0;
    }

    public void Dispose()
    {
        Reset();
        var instances = _instances; _instances = null;
        // Pipelines own bind-group layouts; retire after buffers/textures and recorded draws.
        var capture = _capture; var present = _present;
        if (_device != null) _device.Retire(() => { instances?.Dispose(); capture?.Dispose(); present?.Dispose(); });
        _capture = _present = null;
        _memory.Clear();
    }

    /// <summary>Before any world render pass, on the frame's command encoder.</summary>
    public void Prepare(WebGpuDevice device, CommandEncoder* encoder, TextureManager textures, string gameDataDirectory)
    {
        if (_device != null && !ReferenceEquals(_device, device)) Dispose();
        _device = device;
        _cacheDirectory ??= Path.Combine(gameDataDirectory, "cache", "entity-impostors", "v2");
        if (_generation != textures.ResourceGeneration || (!Enabled && (_requested || _atlas != null)))
        {
            Reset(); _generation = textures.ResourceGeneration;
        }
        _count = LastSubmitted = PendingFallbacks = LastPoseMask = LastHurtSubmitted = 0;
        _capture?.ResetUniformPool(); _present?.ResetUniformPool();
        // Compare the effective upload snapshot as well as the pack token. This catches in-place
        // texture/sampler replacement without trusting a pack display name or mutable file path.
        var texture = Enabled && _requested ? textures.GetTextureId("/mob/cow.png").Texture : null;
        var source = texture?.ImpostorSource;
        if (_source != null && !ReferenceEquals(_source, source)) { Reset(); _requested = Enabled; }
        try { PollCacheWork(device); PollReadback(); }
        catch (Exception ex) { Dispose(); _failed = true; Failures++; s_log.LogError(ex, "Impostor cache installation failed; keeping 3D."); }
        if (_atlas != null && device.ErrorCount != _gpuErrors)
        {
            Dispose(); _failed = true; Failures++;
            s_log.LogError("GPU error during cow impostor lifetime; falling back to 3D.");
        }
        var now = Stopwatch.GetTimestamp();
        var frameMs = _lastPrepare == 0 ? 0 : Stopwatch.GetElapsedTime(_lastPrepare, now).TotalMilliseconds;
        _lastPrepare = now;
        if (!Enabled || !_requested || _failed || source == null) return;
        if (Ready) { StartReadback(device, encoder); return; }
        using var captureTiming = Profiler.Begin("EntityImpostorCaptureCpu");
        try
        {
            if (_source == null && _work == null)
            {
                _source = source;
                _vertices = CowImpostorGeometry.BuildPoses();
                _radius = _vertices.SelectMany(v => v).Max(v => v.Position.Length()) * 1.03f;
                var vertices = _vertices; var radius = _radius; var epoch = _epoch; var cancel = _cancel.Token;
                _workStarted = Stopwatch.GetTimestamp(); _workEpoch = epoch;
                _work = Task.Run(() =>
                {
                    try { cancel.ThrowIfCancellationRequested(); return new CacheWork(epoch, EntityImpostorCache.Key(vertices, source, radius), false, null, null); }
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
            var skin = texture?.Wgpu;
            if (skin == null) return;
            var views = ViewsThisFrame(CompletedViews);
            for (var n = 0; n < views; n++)
            {
                var index = CompletedViews;
                var pose = index / EntityImpostorLayout.Views;
                var view = index % EntityImpostorLayout.Views;
                RenderPassColorAttachment color = new() { View = _atlas!.View, LoadOp = index == 0 ? LoadOp.Clear : LoadOp.Load,
                    StoreOp = StoreOp.Store, ClearValue = default, DepthSlice = uint.MaxValue };
                RenderPassDepthStencilAttachment depth = new() { View = _depthView, DepthLoadOp = LoadOp.Clear,
                    DepthStoreOp = StoreOp.Store, DepthClearValue = 1, StencilLoadOp = LoadOp.Undefined, StencilStoreOp = StoreOp.Undefined };
                RenderPassDescriptor desc = new() { ColorAttachmentCount = 1, ColorAttachments = &color, DepthStencilAttachment = &depth };
                var pass = device.Api.CommandEncoderBeginRenderPass(encoder, in desc);
                try
                {
                    var x = (uint)(view % EntityImpostorLayout.Columns * EntityImpostorLayout.Cell + EntityImpostorLayout.Padding);
                    var y = (uint)((pose * EntityImpostorLayout.RowsPerPose + view / EntityImpostorLayout.Columns) *
                        EntityImpostorLayout.Cell + EntityImpostorLayout.Padding);
                    device.Api.RenderPassEncoderSetViewport(pass, x, y, EntityImpostorLayout.Tile, EntityImpostorLayout.Tile, 0, 1);
                    device.Api.RenderPassEncoderSetScissorRect(pass, x, y, EntityImpostorLayout.Tile, EntityImpostorLayout.Tile);
                    var direction = EntityLodDirections.Get(view);
                    var (_, up) = EntityImpostorLayout.Basis(direction);
                    var matrix = Matrix4x4.CreateLookAt(direction * (_radius * 3), Vector3.Zero, up) *
                        Matrix4x4.CreateOrthographic(_radius * 2, _radius * 2, _radius, _radius * 5);
                    _capture!.Bind(pass);
                    _capture.BindNextUniforms(pass, matrix);
                    WgpuPipeline.BindGroup(pass, 1, skin.BindGroupFor(_capture.TextureBindGroupLayout), device.Api);
                    _geometry![pose].Draw(pass);
                }
                finally { device.Api.RenderPassEncoderEnd(pass); device.Api.RenderPassEncoderRelease(pass); }
                CompletedViews++;
                CapturedViews++;
                if (Stopwatch.GetElapsedTime(captureStarted).TotalMilliseconds >= .5) break;
            }
            if (Ready) StartReadback(device, encoder);
        }
        catch (Exception ex)
        {
            Dispose(); _failed = true; Failures++;
            s_log.LogError(ex, "Cow impostor capture failed; keeping 3D until reset/resource reload.");
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
            var directory = _cacheDirectory!; var epoch = _epoch; var radius = _radius; var cancel = _cancel.Token;
            _workStarted = Stopwatch.GetTimestamp(); _workEpoch = epoch;
            _work = Task.Run(() =>
            {
                try { return new CacheWork(epoch, work.Key, true, EntityImpostorCache.Read(directory, work.Key, radius, cancel), null); }
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
        EnsureResources(device, false);
        _atlas = CreateAtlas(device);
        _atlas.WriteLevel(0, 0, 0, EntityImpostorLayout.Width, EntityImpostorLayout.Height, atlas.Pixels);
        _radius = atlas.Radius; CompletedViews = EntityImpostorLayout.Captures; _persistenceStarted = true;
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
        var atlas = new EntityImpostorCache.Atlas(_key, _radius, pixels);
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

    internal void ClearMemoryForTest() { Reset(); _memory.Clear(); }

    public bool TrySubmit(IEntityLodProvider? provider, EntityLodSelector.Decision decision,
        Vector3 cameraRelativePosition, float yaw, float light, int pose, bool hurt)
    {
        if (!Enabled || provider is not StandingCowLodProvider || decision.Intended != EntityLodTier.Impostor ||
            decision.ViewIndex < 0 || pose is < 0 or >= EntityImpostorLayout.Poses) return false;
        _requested = true;
        if (!Ready || _count == MaxInstances) { PendingFallbacks++; return false; }
        var rotation = Matrix4x4.CreateRotationY(-yaw * MathF.PI / 180);
        var (right, up) = EntityImpostorLayout.Basis(EntityLodDirections.Get(decision.ViewIndex));
        _staging[_count++] = new Instance { Center = new Vector4(cameraRelativePosition, light),
            Right = new Vector4(Vector3.TransformNormal(right, rotation) * _radius, 0),
            Up = new Vector4(Vector3.TransformNormal(up, rotation) * _radius, 0),
            UV = EntityImpostorLayout.UV(decision.ViewIndex, pose),
            Effects = new Vector4(1, 1, 1, hurt ? 0.4f : 0) };
        LastPoseMask |= 1 << pose;
        if (hurt) LastHurtSubmitted++;
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
        _count = 0;
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
                &layout, 1, state, WgpuTexture.Format, WgpuFramebuffer.DepthFormat, label: "Cow impostor capture");
            uniforms[0].Buffer.MinBindingSize = 160;
            BindGroupLayoutEntry[] storage = [new() { Binding = 0, Visibility = ShaderStage.Vertex,
                Buffer = new BufferBindingLayout { Type = BufferBindingType.ReadOnlyStorage } }];
            _present = new WgpuPipeline(device, EntityImpostorShaders.Present, "vs_main", 160, uniforms, storage,
                null, 0, state, device.SurfaceFormat, WgpuFramebuffer.DepthFormat, textureArrayEntries: textures, label: "Cow impostor cutout");
            _instances = new WgpuStorageBuffer(device, MaxInstances * 80, _present.TextureBindGroupLayout);
        }
        if (_atlas != null || !capture) return;
        var vertices = _vertices!;
        _geometry = vertices.Select(pose => new WgpuMesh(device, MemoryMarshal.AsBytes(pose.AsSpan()), 32)).ToArray();
        // Single mip + nearest sampling for this prototype: padded tiles cannot bleed into each
        // other. Alpha-aware mip generation and filtering are required before general rollout.
        _atlas = CreateAtlas(device);
        TextureDescriptor depth = new() { Usage = TextureUsage.RenderAttachment, Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D(EntityImpostorLayout.Width, EntityImpostorLayout.Height, 1), Format = WgpuFramebuffer.DepthFormat, MipLevelCount = 1, SampleCount = 1 };
        _depth = device.Api.DeviceCreateTexture(device.Device, in depth);
        _depthView = device.Api.TextureCreateView(_depth, null);
    }

    private static WgpuTexture CreateAtlas(WebGpuDevice device) => new(device, EntityImpostorLayout.Width, EntityImpostorLayout.Height, 1,
        WgpuSamplerDescription.Nearest with { AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge },
        TextureUsage.RenderAttachment | TextureUsage.CopySrc);
}
