using Silk.NET.WebGPU;

namespace OmniBlock.Client.Rendering.Core.WebGPU;

/// <summary>
///     A 2D texture array with its view and sampler, and the bind groups binding them to the
///     texture group of whatever pipelines sample it.
/// </summary>
/// <remarks>
///     Bind groups are made on demand per layout, for the same reason <see cref="WgpuTexture" />
///     does it: the arrays are built when the texture pack is read, which is long before the
///     pipelines that sample them exist.
/// </remarks>
public sealed unsafe class WgpuTextureArray : IDisposable
{
    /// <summary>The format every layer is uploaded as.</summary>
    public const TextureFormat Format = TextureFormat.Rgba8Unorm;

    private readonly Dictionary<nint, nint> _bindGroups = [];

    private readonly WebGpuDevice _device;
    private bool _disposed;
    private WgpuSamplerDescription _samplerDescription;

    /// <summary>Creates an empty array; the caller fills layers with <see cref="UploadLayer" />.</summary>
    public WgpuTextureArray(WebGpuDevice device, uint width, uint height, uint layerCount,
        WgpuSamplerDescription sampler, uint mipLevelCount = 1)
    {
        _device = device;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        LayerCount = Math.Max(1, layerCount);
        MipLevelCount = Math.Max(1, mipLevelCount);
        var api = device.Api;

        TextureDescriptor desc = new()
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D(Width, Height, LayerCount),
            Format = Format,
            MipLevelCount = MipLevelCount,
            SampleCount = 1
        };

        Texture = api.DeviceCreateTexture(device.Device, in desc);

        TextureViewDescriptor viewDesc = new()
        {
            Format = Format,
            Dimension = TextureViewDimension.Dimension2DArray,
            MipLevelCount = MipLevelCount,
            ArrayLayerCount = LayerCount,
            Aspect = TextureAspect.All
        };

        View = api.TextureCreateView(Texture, in viewDesc);

        _samplerDescription = sampler;
        Sampler = CreateSampler(sampler);
    }

    /// <summary>Creates a square array bound against one layout up front.</summary>
    public WgpuTextureArray(WebGpuDevice device, uint layerSize, uint layerCount,
        BindGroupLayout* textureBindGroupLayout)
        : this(device, layerSize, layerSize, layerCount, WgpuSamplerDescription.Nearest) =>
        BindGroup = BindGroupFor(textureBindGroupLayout);

    public Texture* Texture { get; }
    public TextureView* View { get; }
    public Sampler* Sampler { get; private set; }

    public uint Width { get; }
    public uint Height { get; }
    public uint LayerCount { get; }
    public uint MipLevelCount { get; }

    /// <summary>The bind group made by the single-layout constructor.</summary>
    public BindGroup* BindGroup { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // The pack switch this defers for is the one that rebuilds these arrays: NamedTextureArray
        // reallocates every layer at the new resolution while the frame is still being recorded.
        WgpuRelease.Deferred(_device, [.. _bindGroups.Values], (nint)Sampler, (nint)View, (nint)Texture);
        _bindGroups.Clear();
    }

    /// <summary>
    ///     Replaces the filtering and wrap rules, rebuilding the sampler and dropping the bind
    ///     groups that referenced the old one. A no-op when nothing changed.
    /// </summary>
    public void SetSampler(WgpuSamplerDescription sampler)
    {
        if (_samplerDescription == sampler) return;
        _samplerDescription = sampler;

        WgpuRelease.Deferred(_device, [.. _bindGroups.Values], (nint)Sampler, 0, 0);
        _bindGroups.Clear();

        Sampler = CreateSampler(sampler);
    }

    /// <summary>The bind group binding this array and its sampler for a pipeline using <paramref name="layout" />.</summary>
    public BindGroup* BindGroupFor(BindGroupLayout* layout)
    {
        if (_bindGroups.TryGetValue((nint)layout, out var cached)) return (BindGroup*)cached;

        var entries = stackalloc BindGroupEntry[2];
        entries[0] = new BindGroupEntry
        {
            Binding = 0,
            TextureView = View
        };
        entries[1] = new BindGroupEntry
        {
            Binding = 1,
            Sampler = Sampler
        };

        BindGroupDescriptor bgDesc = new()
        {
            Layout = layout,
            EntryCount = 2,
            Entries = entries
        };

        var bindGroup = _device.Api.DeviceCreateBindGroup(_device.Device, in bgDesc);
        _bindGroups[(nint)layout] = (nint)bindGroup;
        return bindGroup;
    }

    /// <summary>Uploads one whole layer's RGBA8 pixels.</summary>
    public void UploadLayer(uint layerIndex, ReadOnlySpan<byte> rgba) =>
        UploadRegion(0, 0, layerIndex, Width, Height, rgba);

    /// <summary>Uploads one filtered level of a whole layer.</summary>
    public void UploadMipLayer(uint layerIndex, uint mipLevel, ReadOnlySpan<byte> rgba) =>
        UploadRegion(0, 0, layerIndex,
            Math.Max(1u, Width >> (int)mipLevel), Math.Max(1u, Height >> (int)mipLevel),
            rgba, mipLevel);

    /// <summary>Uploads a sub-rectangle of one layer — a texture-pack override, or an animated tile tick.</summary>
    public void UploadRegion(uint x, uint y, uint layerIndex, uint width, uint height,
        ReadOnlySpan<byte> rgba, uint mipLevel = 0)
    {
        if (width == 0 || height == 0 || layerIndex >= LayerCount || mipLevel >= MipLevelCount) return;

        var rows = WgpuPixelRows.Align(rgba, width, height, out var bytesPerRow);
        var api = _device.Api;

        ImageCopyTexture destination = new()
        {
            Texture = Texture,
            MipLevel = mipLevel,
            Origin = new Origin3D(x, y, layerIndex),
            Aspect = TextureAspect.All
        };

        TextureDataLayout layout = new()
        {
            Offset = 0,
            BytesPerRow = bytesPerRow,
            RowsPerImage = height
        };

        Extent3D extent = new(width, height, 1);

        fixed (byte* p = rows)
        {
            api.QueueWriteTexture(_device.Queue, in destination, p, (nuint)rows.Length, in layout, in extent);
        }
    }

    private Sampler* CreateSampler(WgpuSamplerDescription description)
    {
        SamplerDescriptor samplerDesc = new()
        {
            AddressModeU = description.AddressU,
            AddressModeV = description.AddressV,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = description.Mag,
            MinFilter = description.Min,
            MipmapFilter = description.Mipmap,
            LodMinClamp = 0.0f,
            LodMaxClamp = Math.Max(0.0f, description.LodMaxClamp),
            MaxAnisotropy = (ushort)Math.Max(1u, description.MaxAnisotropy)
        };

        return _device.Api.DeviceCreateSampler(_device.Device, in samplerDesc);
    }
}
