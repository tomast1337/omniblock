using System.Numerics;
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
/// Opt-in, one-model/one-pose in-memory prototype. All GPU work is on the render thread;
/// candidates become sampleable only after all 26 passes have been recorded before the world pass.
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
    private WgpuMesh? _geometry;
    private WgpuPipeline? _capture, _present;
    private WgpuStorageBuffer? _instances;
    private readonly Instance[] _staging = new Instance[MaxInstances];
    private int _count;
    private bool _requested, _failed;
    private long _generation = -1;
    private long _gpuErrors;
    private float _radius;
    public bool Enabled { get; set; }
    public bool ForceTierForTest { get; set; }
    public int CompletedViews { get; private set; }
    public bool Ready => _atlas != null && MayPublish(CompletedViews, _failed);
    public int LastSubmitted { get; private set; }
    public int Failures { get; private set; }
    public int Replacements { get; private set; }
    public int PendingFallbacks { get; private set; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Instance { public Vector4 Center, Right, Up, UV; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Uniforms { public Matrix4x4 View, Projection; public Vector4 FogColor, Fog; }

    // GPU-free state gates are also tested without a graphics device.
    internal static bool MayPublish(int completed, bool failed) => completed == EntityImpostorLayout.Views && !failed;
    internal static int ViewsThisFrame(int completed) => Math.Clamp(EntityImpostorLayout.Views - completed, 0, 2);

    public void Reset()
    {
        if (_atlas != null) Replacements++;
        _atlas?.Dispose(); _atlas = null;
        _geometry?.Dispose(); _geometry = null;
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
    }

    /// <summary>Before any world render pass, on the frame's command encoder.</summary>
    public void Prepare(WebGpuDevice device, CommandEncoder* encoder, TextureManager textures)
    {
        if (_device != null && !ReferenceEquals(_device, device)) Dispose();
        _device = device;
        if (_generation != textures.ResourceGeneration || !Enabled)
        {
            Reset(); _generation = textures.ResourceGeneration;
        }
        _count = LastSubmitted = PendingFallbacks = 0;
        _capture?.ResetUniformPool(); _present?.ResetUniformPool();
        if (_atlas != null && device.ErrorCount != _gpuErrors)
        {
            Dispose(); _failed = true; Failures++;
            s_log.LogError("GPU error during cow impostor lifetime; falling back to 3D.");
        }
        if (!Enabled || !_requested || _failed || Ready) return;
        using var captureTiming = Profiler.Begin("EntityImpostorCaptureCpu");
        try
        {
            EnsureResources(device);
            if (device.ErrorCount != _gpuErrors) throw new InvalidOperationException("GPU rejected the impostor resources.");
            var skin = textures.GetTextureId("/mob/cow.png").Texture?.Wgpu;
            if (skin == null) return;
            var views = ViewsThisFrame(CompletedViews);
            for (var n = 0; n < views; n++)
            {
                var index = CompletedViews;
                RenderPassColorAttachment color = new() { View = _atlas!.View, LoadOp = index == 0 ? LoadOp.Clear : LoadOp.Load,
                    StoreOp = StoreOp.Store, ClearValue = default, DepthSlice = uint.MaxValue };
                RenderPassDepthStencilAttachment depth = new() { View = _depthView, DepthLoadOp = LoadOp.Clear,
                    DepthStoreOp = StoreOp.Store, DepthClearValue = 1, StencilLoadOp = LoadOp.Undefined, StencilStoreOp = StoreOp.Undefined };
                RenderPassDescriptor desc = new() { ColorAttachmentCount = 1, ColorAttachments = &color, DepthStencilAttachment = &depth };
                var pass = device.Api.CommandEncoderBeginRenderPass(encoder, in desc);
                try
                {
                    var x = (uint)(index % EntityImpostorLayout.Columns * EntityImpostorLayout.Cell + EntityImpostorLayout.Padding);
                    var y = (uint)(index / EntityImpostorLayout.Columns * EntityImpostorLayout.Cell + EntityImpostorLayout.Padding);
                    device.Api.RenderPassEncoderSetViewport(pass, x, y, EntityImpostorLayout.Tile, EntityImpostorLayout.Tile, 0, 1);
                    device.Api.RenderPassEncoderSetScissorRect(pass, x, y, EntityImpostorLayout.Tile, EntityImpostorLayout.Tile);
                    var direction = EntityLodDirections.Get(index);
                    var (_, up) = EntityImpostorLayout.Basis(direction);
                    var matrix = Matrix4x4.CreateLookAt(direction * (_radius * 3), Vector3.Zero, up) *
                        Matrix4x4.CreateOrthographic(_radius * 2, _radius * 2, _radius, _radius * 5);
                    _capture!.Bind(pass);
                    _capture.BindNextUniforms(pass, matrix);
                    WgpuPipeline.BindGroup(pass, 1, skin.BindGroupFor(_capture.TextureBindGroupLayout), device.Api);
                    _geometry!.Draw(pass);
                }
                finally { device.Api.RenderPassEncoderEnd(pass); device.Api.RenderPassEncoderRelease(pass); }
                CompletedViews++;
            }
        }
        catch (Exception ex)
        {
            Dispose(); _failed = true; Failures++;
            s_log.LogError(ex, "Cow impostor capture failed; keeping 3D until reset/resource reload.");
        }
    }

    public bool TrySubmit(IEntityLodProvider? provider, EntityLodSelector.Decision decision,
        Vector3 cameraRelativePosition, float yaw, float light)
    {
        if (!Enabled || provider is not StandingCowLodProvider || decision.Intended != EntityLodTier.Impostor || decision.ViewIndex < 0) return false;
        _requested = true;
        if (!Ready || _count == MaxInstances) { PendingFallbacks++; return false; }
        var rotation = Matrix4x4.CreateRotationY(-yaw * MathF.PI / 180);
        var (right, up) = EntityImpostorLayout.Basis(EntityLodDirections.Get(decision.ViewIndex));
        _staging[_count++] = new Instance { Center = new Vector4(cameraRelativePosition, light),
            Right = new Vector4(Vector3.TransformNormal(right, rotation) * _radius, 0),
            Up = new Vector4(Vector3.TransformNormal(up, rotation) * _radius, 0), UV = EntityImpostorLayout.UV(decision.ViewIndex) };
        return true;
    }

    public void Draw()
    {
        if (_count == 0 || !Ready || RenderSystem.DrawTarget is not WebGpuDrawTarget target) return;
        var started = EntityPresentationMetrics.StartTimer();
        var fog = RenderSystem.Fog;
        _instances!.Write<Instance>(_staging.AsSpan(0, _count));
        EntityPresentationMetrics.Uploaded(_count * 64);
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

    private void EnsureResources(WebGpuDevice device)
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
            _instances = new WgpuStorageBuffer(device, MaxInstances * 64, _present.TextureBindGroupLayout);
        }
        if (_atlas != null) return;
        var vertices = CowImpostorGeometry.Build();
        _radius = vertices.Max(v => v.Position.Length()) * 1.03f;
        _geometry = new WgpuMesh(device, MemoryMarshal.AsBytes(vertices.AsSpan()), 32);
        // Single mip + nearest sampling for this prototype: padded tiles cannot bleed into each
        // other. Alpha-aware mip generation and filtering are required before general rollout.
        _atlas = new WgpuTexture(device, EntityImpostorLayout.Width, EntityImpostorLayout.Height, 1,
            WgpuSamplerDescription.Nearest with { AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge }, TextureUsage.RenderAttachment);
        TextureDescriptor depth = new() { Usage = TextureUsage.RenderAttachment, Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D(EntityImpostorLayout.Width, EntityImpostorLayout.Height, 1), Format = WgpuFramebuffer.DepthFormat, MipLevelCount = 1, SampleCount = 1 };
        _depth = device.Api.DeviceCreateTexture(device.Device, in depth);
        _depthView = device.Api.TextureCreateView(_depth, null);
    }
}
