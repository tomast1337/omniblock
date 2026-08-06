using Silk.NET.WebGPU;

namespace BetaSharp.Client.Rendering.Core.WebGPU;

/// <summary>
///     A 2D texture array with one sampler, and the bind group that hooks them to group 1 of the
///     chunk shader.
/// </summary>
public sealed unsafe class WgpuTextureArray : IDisposable
{
    public Texture* Texture { get; }
    public TextureView* View { get; }
    public Sampler* Sampler { get; }

    /// <summary>The bind group at group 1 binding the array and sampler.</summary>
    public BindGroup* BindGroup { get; }

    public uint LayerCount { get; }
    public uint LayerSize { get; }

    private readonly WebGpuDevice _device;
    private bool _disposed;

    /// <summary>
    ///     Creates an array where each layer is a <paramref name="layerSize"/>×<paramref name="layerSize"/>
    ///     RGBA8 image. The caller uploads each layer with <see cref="UploadLayer"/>.
    /// </summary>
    public WgpuTextureArray(WebGpuDevice device, uint layerSize, uint layerCount,
        BindGroupLayout* textureBindGroupLayout)
    {
        _device = device;
        LayerSize = layerSize;
        LayerCount = layerCount;
        Silk.NET.WebGPU.WebGPU api = device.Api;

        TextureDescriptor desc = new()
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D(layerSize, layerSize, layerCount),
            Format = TextureFormat.Rgba8Unorm,
            MipLevelCount = 1,
            SampleCount = 1,
        };

        Texture = api.DeviceCreateTexture(device.Device, in desc);

        TextureViewDescriptor viewDesc = new()
        {
            Format = TextureFormat.Rgba8Unorm,
            Dimension = TextureViewDimension.Dimension2DArray,
            MipLevelCount = 1,
            ArrayLayerCount = layerCount,
            Aspect = TextureAspect.All,
        };

        View = api.TextureCreateView(Texture, in viewDesc);

        SamplerDescriptor samplerDesc = new()
        {
            AddressModeU = AddressMode.Repeat,
            AddressModeV = AddressMode.Repeat,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Nearest,
            MinFilter = FilterMode.Nearest,
            MipmapFilter = MipmapFilterMode.Nearest,
            LodMinClamp = 0.0f,
            LodMaxClamp = 1.0f,
            MaxAnisotropy = 1,
        };

        Sampler = api.DeviceCreateSampler(device.Device, in samplerDesc);

        BindGroupEntry* entries = stackalloc BindGroupEntry[2];
        entries[0] = new BindGroupEntry { Binding = 0, TextureView = View };
        entries[1] = new BindGroupEntry { Binding = 1, Sampler = Sampler };

        BindGroupDescriptor bgDesc = new()
        {
            Layout = textureBindGroupLayout,
            EntryCount = 2,
            Entries = entries,
        };

        BindGroup = api.DeviceCreateBindGroup(device.Device, in bgDesc);
    }

    /// <summary>Uploads one layer's RGBA8 pixels.</summary>
    public void UploadLayer(uint layerIndex, ReadOnlySpan<byte> rgba)
    {
        Silk.NET.WebGPU.WebGPU api = _device.Api;

        ImageCopyTexture destination = new()
        {
            Texture = Texture,
            MipLevel = 0,
            Origin = new Origin3D(0, 0, layerIndex),
            Aspect = TextureAspect.All,
        };

        TextureDataLayout layout = new()
        {
            Offset = 0,
            BytesPerRow = LayerSize * 4,
            RowsPerImage = LayerSize,
        };

        Extent3D extent = new(LayerSize, LayerSize, 1);

        fixed (byte* p = rgba)
        {
            api.QueueWriteTexture(_device.Queue, in destination, p, (nuint)rgba.Length, in layout, in extent);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Silk.NET.WebGPU.WebGPU api = _device.Api;
        if (BindGroup is not null) api.BindGroupRelease(BindGroup);
        if (Sampler is not null) api.SamplerRelease(Sampler);
        if (View is not null) api.TextureViewRelease(View);
        if (Texture is not null) { api.TextureDestroy(Texture); api.TextureRelease(Texture); }
    }
}
