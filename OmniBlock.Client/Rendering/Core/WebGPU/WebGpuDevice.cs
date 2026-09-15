using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Silk.NET.Core.Contexts;
using Silk.NET.WebGPU;

namespace OmniBlock.Client.Rendering.Core.WebGPU;

/// <summary>
///     The WebGPU instance, surface, adapter, device and queue, and the surface's configuration.
/// </summary>
/// <remarks>
///     <para>
///         One of these exists per window and outlives every renderer, because everything a
///         renderer creates — buffers, textures, pipelines — belongs to the device and dies with
///         it.
///     </para>
///     <para>
///         Adapter and device requests are asynchronous in the WebGPU API and synchronous in every
///         native implementation of it: the callback runs before the request call returns. This
///         waits on that rather than pumping an event loop, which is correct for wgpu-native and
///         Dawn and would deadlock only in a browser, where nothing here runs.
///     </para>
/// </remarks>
public sealed unsafe class WebGpuDevice : IDisposable
{
    private static readonly ILogger s_logger = Log.Instance.For<WebGpuDevice>();
    private readonly CompositeAlphaMode _alphaMode;

    /// <summary>Held so the GC cannot collect the thunk while wgpu still holds the pointer.</summary>
    private readonly PfnErrorCallback _errorCallback;

    private PresentMode _presentMode;
    private bool? _pendingVSync;

    /// <summary>Releases waiting for the frame that may have recorded against them to be submitted.</summary>
    private readonly List<Action> _retired = [];

    /// <summary>The encoder the current frame is recording into, released when the next one replaces it.</summary>
    private CommandEncoder* _commandEncoder;

    private bool _disposed;

    /// <summary>
    ///     The texture behind the frame currently being drawn, held until it has been presented.
    /// </summary>
    /// <remarks>
    ///     A view does not keep its texture alive at this level, so releasing the surface texture
    ///     as soon as the view exists leaves the render pass writing to a destroyed resource — a
    ///     validation error at submit rather than at the release that caused it.
    /// </remarks>
    private Texture* _frameTexture;

    private WebGpuDevice(INativeWindowSource window, uint width, uint height, bool vsync)
    {
        Api = Silk.NET.WebGPU.WebGPU.GetApi();

        InstanceDescriptor instanceDescriptor = default;
        Instance = Api.CreateInstance(in instanceDescriptor);
        if (Instance is null)
        {
            throw new InvalidOperationException("WebGPU instance creation failed.");
        }

        Surface = window.CreateWebGPUSurface(Api, Instance);
        if (Surface is null)
        {
            throw new InvalidOperationException(
                "WebGPU surface creation failed. The window must have been created with no client " +
                "API, and the platform must be one Silk.NET has a surface descriptor for.");
        }

        Adapter = RequestAdapter();
        var adapterSupportsTimestampQueries = Api.AdapterHasFeature(Adapter, FeatureName.TimestampQuery);
        Device = RequestDevice(Adapter, adapterSupportsTimestampQueries, out var timestampQueriesEnabled);
        Queue = Api.DeviceGetQueue(Device);
        GpuProfiler = new GpuFrameProfiler(this, timestampQueriesEnabled);
        QuadIndices = new SharedQuadIndexBuffer(this);
        QuadWireframeIndices = new SharedQuadWireframeIndexBuffer(this);

        _errorCallback = new PfnErrorCallback(OnUncapturedError);
        Api.DeviceSetUncapturedErrorCallback(Device, _errorCallback, null);

        (SurfaceFormat, _presentMode, _alphaMode) = ChooseSurfaceConfiguration(vsync);

        s_logger.LogInformation(
            "WebGPU device ready: surface format {Format}, present mode {PresentMode}, surface {Width}x{Height}; GPU timestamps: {TimestampStatus}.",
            SurfaceFormat, _presentMode, width, height, GpuProfiler.Latest.Status);

        Configure(width, height);
    }

    public Silk.NET.WebGPU.WebGPU Api { get; }
    public Instance* Instance { get; }
    public Surface* Surface { get; }
    public Adapter* Adapter { get; }
    public Device* Device { get; }
    public Queue* Queue { get; }
    internal GpuFrameProfiler GpuProfiler { get; }
    internal SharedQuadIndexBuffer QuadIndices { get; }
    internal SharedQuadWireframeIndexBuffer QuadWireframeIndices { get; }

    /// <summary>The format the surface's textures are in, and so the format every pipeline that draws to the screen must target.</summary>
    public TextureFormat SurfaceFormat { get; }

    public uint Width { get; private set; }
    public uint Height { get; private set; }

    /// <summary>The device when the backend is WebGPU; null otherwise and during the first frame before creation.</summary>
    public static WebGpuDevice? Current { get; private set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Before the flag, so what is waiting still runs: nothing else is going to submit a frame.
        DrainRetired();

        _disposed = true;

        if (Current == this) Current = null;

        if (_commandEncoder is not null)
        {
            Api.CommandEncoderRelease(_commandEncoder);
            _commandEncoder = null;
        }

        GpuProfiler.Dispose();
        QuadWireframeIndices.Dispose();
        QuadIndices.Dispose();

        if (Queue is not null) Api.QueueRelease(Queue);
        if (Device is not null) Api.DeviceRelease(Device);
        if (Adapter is not null) Api.AdapterRelease(Adapter);
        if (Surface is not null) Api.SurfaceRelease(Surface);
        if (Instance is not null) Api.InstanceRelease(Instance);

        Api.Dispose();
    }

    /// <summary>
    ///     Creates a fresh command encoder for the current frame. The previous encoder is
    ///     released; call this once per frame before encoding commands.
    /// </summary>
    public CommandEncoder* CreateCommandEncoder()
    {
        // The handle has to be stored, not just returned: releasing a local copy that was never
        // updated freed the same first-frame encoder on every frame, and a freed wgpu handle
        // released again corrupts the allocator rather than failing.
        if (_commandEncoder is not null)
        {
            Api.CommandEncoderRelease(_commandEncoder);
            _commandEncoder = null;
        }

        CommandEncoderDescriptor descriptor = default;
        _commandEncoder = Api.DeviceCreateCommandEncoder(Device, in descriptor);
        return _commandEncoder;
    }

    public static WebGpuDevice Create(INativeWindowSource window, int width, int height, bool vsync = true)
    {
        WebGpuDevice device = new(window, (uint)Math.Max(1, width), (uint)Math.Max(1, height), vsync);
        Current = device;
        return device;
    }

    /// <summary>
    ///     Requests a presentation-mode change at the next acquire boundary. Reconfiguring while
    ///     the current surface texture is being encoded is invalid, so option changes are not
    ///     applied synchronously from the UI callback.
    /// </summary>
    public void SetVSyncEnabled(bool enabled) => _pendingVSync = enabled;

    /// <summary>Points the surface at a new size. Must be called after every resize, or acquiring a texture fails.</summary>
    public void Configure(uint width, uint height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);

        SurfaceConfiguration configuration = new()
        {
            Device = Device,
            Format = SurfaceFormat,
            Usage = TextureUsage.RenderAttachment,
            AlphaMode = _alphaMode,
            Width = Width,
            Height = Height,
            PresentMode = _presentMode
        };

        Api.SurfaceConfigure(Surface, in configuration);
    }

    /// <summary>
    ///     The view this frame draws into, or null when the surface needs reconfiguring first —
    ///     which happens on a resize the window system saw before we did.
    /// </summary>
    /// <remarks>
    ///     A null return is not an error and the caller should skip the frame; the next one gets a
    ///     texture. <see cref="Present" /> must not be called for a skipped frame.
    /// </remarks>
    public TextureView* AcquireFrame()
    {
        ReleaseFrameTexture();
        ApplyPendingPresentMode();

        SurfaceTexture surfaceTexture = default;
        Api.SurfaceGetCurrentTexture(Surface, ref surfaceTexture);

        switch (surfaceTexture.Status)
        {
            case SurfaceGetCurrentTextureStatus.Success:
                break;

            case SurfaceGetCurrentTextureStatus.Timeout:
            case SurfaceGetCurrentTextureStatus.Outdated:
            case SurfaceGetCurrentTextureStatus.Lost:
                if (surfaceTexture.Texture is not null)
                {
                    Api.TextureRelease(surfaceTexture.Texture);
                }

                Configure(Width, Height);
                return null;

            default:
                throw new InvalidOperationException(
                    $"Acquiring the surface texture failed with {surfaceTexture.Status}.");
        }

        TextureViewDescriptor viewDescriptor = new()
        {
            Format = SurfaceFormat,
            Dimension = TextureViewDimension.Dimension2D,
            MipLevelCount = 1,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All
        };

        _frameTexture = surfaceTexture.Texture;
        return Api.TextureCreateView(surfaceTexture.Texture, in viewDescriptor);
    }

    /// <summary>
    ///     Holds a release back until the frame being recorded has been submitted.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Destroying a texture the current encoder has already recorded a draw against fails
    ///         validation when that encoder is finished — and a texture pack can be switched from
    ///         the options screen, which is drawn in the middle of the frame whose terrain pass just
    ///         sampled the arrays being rebuilt.
    ///     </para>
    ///     <para>
    ///         One frame is enough, and no fence is needed: work already submitted keeps its own
    ///         reference to everything it uses, so a release after the submit is safe however long
    ///         the GPU takes to get to it.
    ///     </para>
    /// </remarks>
    public void Retire(Action release)
    {
        // Past disposal there is no frame left to wait for, and the device is about to take
        // everything with it.
        if (_disposed) return;

        _retired.Add(release);
    }

    public void Present()
    {
        Api.SurfacePresent(Surface);
        ReleaseFrameTexture();
        DrainRetired();
    }

    /// <summary>
    ///     Blocks until the GPU has caught up with every submission so far, running due callbacks
    ///     (a buffer map's, for one) along the way.
    /// </summary>
    /// <remarks>
    ///     <c>wgpuInstanceProcessEvents</c> — the portable webgpu.h call for pumping callbacks — is
    ///     an unimplemented stub in this wgpu-native build and aborts the process outright, not
    ///     throws, on the mere act of calling it. <c>wgpuDevicePoll</c> is wgpu-native's own,
    ///     original polling entry point, predating that portable one; it is not part of the
    ///     standard webgpu.h Silk.NET.WebGPU binds, which is why it needs its own P/Invoke here
    ///     instead of a call through <see cref="Api" />, but the native library both are already
    ///     loaded from exports it all the same.
    /// </remarks>
    public void Poll() => WgpuDevicePoll(Device, 1, 0);
    /// <summary>Dispatch ready callbacks without waiting for GPU completion.</summary>
    internal void PollNonBlocking() => WgpuDevicePoll(Device, 0, 0);

    [DllImport("wgpu_native", EntryPoint = "wgpuDevicePoll")]
    private static extern void WgpuDevicePoll(Device* device, uint wait, nint wrappedSubmissionIndex);

    private void DrainRetired()
    {
        if (_retired.Count == 0) return;

        // Copied first: a release may retire something of its own, which belongs to the next frame.
        Action[] releases = [.. _retired];
        _retired.Clear();

        foreach (var release in releases) release();
    }

    private void ReleaseFrameTexture()
    {
        if (_frameTexture is not null)
        {
            Api.TextureRelease(_frameTexture);
            _frameTexture = null;
        }
    }

    private Adapter* RequestAdapter()
    {
        Adapter* adapter = null;
        string? message = null;

        RequestAdapterOptions options = new()
        {
            CompatibleSurface = Surface,
            PowerPreference = PowerPreference.HighPerformance
        };

        PfnRequestAdapterCallback callback = new((status, result, error, _) =>
        {
            if (status == RequestAdapterStatus.Success)
            {
                adapter = result;
            }
            else
            {
                message = $"{status}: {Marshal.PtrToStringUTF8((nint)error)}";
            }
        });

        Api.InstanceRequestAdapter(Instance, in options, callback, null);

        return adapter is null
            ? throw new InvalidOperationException($"No WebGPU adapter available. {message}")
            : adapter;
    }

    private Device* RequestDevice(Adapter* adapter, bool requestTimestampQueries, out bool timestampQueriesEnabled)
    {
        var device = TryRequestDevice(adapter, requestTimestampQueries, out var message);
        if (device is not null)
        {
            timestampQueriesEnabled = requestTimestampQueries &&
                                      Api.DeviceHasFeature(device, FeatureName.TimestampQuery);
            return device;
        }

        if (requestTimestampQueries)
        {
            s_logger.LogWarning(
                "WebGPU device creation rejected optional timestamp queries ({Message}); retrying without GPU timing.",
                message);
            device = TryRequestDevice(adapter, false, out message);
        }

        timestampQueriesEnabled = false;
        return device is null
            ? throw new InvalidOperationException($"WebGPU device creation failed. {message}")
            : device;
    }

    private Device* TryRequestDevice(Adapter* adapter, bool requestTimestampQueries, out string? message)
    {
        Device* device = null;
        string? requestMessage = null;

        DeviceDescriptor descriptor = default;
        FeatureName timestampQuery = FeatureName.TimestampQuery;
        if (requestTimestampQueries)
        {
            descriptor.RequiredFeatureCount = 1;
            descriptor.RequiredFeatures = &timestampQuery;
        }

        PfnRequestDeviceCallback callback = new((status, result, error, _) =>
        {
            if (status == RequestDeviceStatus.Success)
            {
                device = result;
            }
            else
            {
                requestMessage = $"{status}: {Marshal.PtrToStringUTF8((nint)error)}";
            }
        });

        Api.AdapterRequestDevice(adapter, in descriptor, callback, null);

        message = requestMessage;
        return device;
    }

    /// <summary>
    ///     Picks a non-sRGB surface format when the surface offers one.
    /// </summary>
    /// <remarks>
    ///     Colours reaching the swap chain are already in the space they should be displayed in —
    ///     the GL path wrote them to a plain framebuffer and this has to match, or everything is
    ///     gamma-corrected twice and washes out. Only if no linear format is offered does the sRGB
    ///     one get taken, and then the difference is visible.
    /// </remarks>
    private (TextureFormat Format, PresentMode PresentMode, CompositeAlphaMode AlphaMode) ChooseSurfaceConfiguration(
        bool vsync)
    {
        SurfaceCapabilities capabilities = default;
        Api.SurfaceGetCapabilities(Surface, Adapter, ref capabilities);

        try
        {
            Span<TextureFormat> formats = new(capabilities.Formats, (int)capabilities.FormatCount);
            Span<PresentMode> presentModes = new(capabilities.PresentModes, (int)capabilities.PresentModeCount);
            Span<CompositeAlphaMode> alphaModes = new(capabilities.AlphaModes, (int)capabilities.AlphaModeCount);

            var format = TextureFormat.Undefined;
            foreach (var candidate in formats)
            {
                if (candidate is TextureFormat.Bgra8Unorm or TextureFormat.Rgba8Unorm)
                {
                    format = candidate;
                    break;
                }
            }

            if (format == TextureFormat.Undefined)
            {
                format = formats.Length > 0
                    ? formats[0]
                    : Api.SurfaceGetPreferredFormat(Surface, Adapter);
            }

            var presentMode = ChoosePresentMode(presentModes, vsync);

            // Opaque explicitly, not alphaModes[0]: the window is never meant to be see-through, and
            // whatever ends up in the swap chain's alpha channel — the cloud blur composite writes
            // less than 1 wherever a cloud fades, see cloud_blur.wgsl — must not be handed to the
            // compositor to blend against the desktop. Driver-reported order isn't Opaque-first on
            // every platform, so picking alphaModes[0] blind can silently pick a mode that does.
            var alphaMode = alphaModes.Contains(CompositeAlphaMode.Opaque)
                ? CompositeAlphaMode.Opaque
                : alphaModes.Length == 0
                    ? CompositeAlphaMode.Auto
                    : alphaModes[0];

            return (format, presentMode, alphaMode);
        }
        finally
        {
            Api.SurfaceCapabilitiesFreeMembers(capabilities);
        }
    }

    internal static PresentMode ChoosePresentMode(ReadOnlySpan<PresentMode> supported, bool vsync)
    {
        if (vsync)
            return supported.Contains(PresentMode.Fifo) || supported.IsEmpty
                ? PresentMode.Fifo
                : supported[0];

        // Mailbox renders without blocking on the display while retaining tear-free presentation.
        // Immediate is the next best uncapped choice; FIFO variants remain the portable fallback.
        if (supported.Contains(PresentMode.Mailbox)) return PresentMode.Mailbox;
        if (supported.Contains(PresentMode.Immediate)) return PresentMode.Immediate;
        if (supported.Contains(PresentMode.FifoRelaxed)) return PresentMode.FifoRelaxed;
        return supported.Contains(PresentMode.Fifo) || supported.IsEmpty
            ? PresentMode.Fifo
            : supported[0];
    }

    private void ApplyPendingPresentMode()
    {
        if (_pendingVSync is not { } vsync) return;
        _pendingVSync = null;

        var (_, requestedMode, _) = ChooseSurfaceConfiguration(vsync);
        if (requestedMode == _presentMode) return;

        _presentMode = requestedMode;
        Configure(Width, Height);
        s_logger.LogInformation("WebGPU present mode changed to {PresentMode}.", _presentMode);
    }

    private long _errorCount;
    public long ErrorCount => Interlocked.Read(ref _errorCount);

    private void OnUncapturedError(ErrorType type, byte* message, void* _)
    {
        Interlocked.Increment(ref _errorCount);
        s_logger.LogError("WebGPU {ErrorType}: {Message}", type, Marshal.PtrToStringUTF8((nint)message));
    }
}
