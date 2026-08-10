using Silk.NET.WebGPU;

namespace OmniBlock.Client.Rendering.Core.WebGPU;

/// <summary>How a texture is filtered and how it behaves outside [0,1].</summary>
/// <remarks>
///     OpenGL hangs these off the texture object and lets them be changed at any point; WebGPU
///     puts them in an immutable sampler. Carrying them as data lets a caller keep setting them
///     the way GL allows, and the sampler is rebuilt underneath when they actually change.
/// </remarks>
public readonly record struct WgpuSamplerDescription(
    FilterMode Mag,
    FilterMode Min,
    MipmapFilterMode Mipmap,
    AddressMode AddressU,
    AddressMode AddressV,
    float LodMaxClamp,
    uint MaxAnisotropy)
{
    /// <summary>Nearest and repeating — Beta's default, and what most of the game's textures want.</summary>
    public static WgpuSamplerDescription Nearest { get; } = new(
        FilterMode.Nearest, FilterMode.Nearest, MipmapFilterMode.Nearest,
        AddressMode.Repeat, AddressMode.Repeat, 0.0f, 1);

    public static WgpuSamplerDescription Linear { get; } = Nearest with
    {
        Mag = FilterMode.Linear,
        Min = FilterMode.Linear,
        Mipmap = MipmapFilterMode.Linear,
    };
}

/// <summary>
///     A 2D texture with its view and sampler, and the bind groups binding them to the texture
///     group of whatever pipelines sample it.
/// </summary>
/// <remarks>
///     A bind group is built against one pipeline's layout, but textures are created long before
///     any pipeline exists — the game loads its textures during startup. So a bind group is made
///     on demand, per layout, rather than in the constructor.
/// </remarks>
public sealed unsafe class WgpuTexture : IDisposable
{
    public Texture* Texture { get; }
    public TextureView* View { get; }
    public Sampler* Sampler { get; private set; }

    public uint Width { get; }
    public uint Height { get; }
    public uint MipLevelCount { get; }

    /// <summary>The format every texture in this path is uploaded as.</summary>
    public const TextureFormat Format = TextureFormat.Rgba8Unorm;

    private readonly WebGpuDevice _device;
    private readonly Dictionary<nint, nint> _bindGroups = [];
    private WgpuSamplerDescription _samplerDescription;
    private bool _disposed;

    /// <summary>Creates an empty texture; the caller fills levels with <see cref="WriteLevel" />.</summary>
    public WgpuTexture(WebGpuDevice device, uint width, uint height, uint mipLevelCount,
        WgpuSamplerDescription sampler)
    {
        _device = device;
        Width = width;
        Height = height;
        MipLevelCount = Math.Max(1, mipLevelCount);
        Silk.NET.WebGPU.WebGPU api = device.Api;

        TextureDescriptor texDesc = new()
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D(width, height, 1),
            Format = Format,
            MipLevelCount = MipLevelCount,
            SampleCount = 1,
        };

        Texture = api.DeviceCreateTexture(device.Device, in texDesc);

        TextureViewDescriptor viewDesc = new()
        {
            Format = Format,
            Dimension = TextureViewDimension.Dimension2D,
            MipLevelCount = MipLevelCount,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All,
        };

        View = api.TextureCreateView(Texture, in viewDesc);

        _samplerDescription = sampler;
        Sampler = CreateSampler(sampler);
    }

    /// <summary>Creates a single-level texture from RGBA8 pixel data, bound against one layout.</summary>
    public WgpuTexture(WebGpuDevice device, uint width, uint height, ReadOnlySpan<byte> rgba,
        BindGroupLayout* textureBindGroupLayout)
        : this(device, width, height, 1, WgpuSamplerDescription.Linear)
    {
        WriteLevel(0, 0, 0, width, height, rgba);
        BindGroup = BindGroupFor(textureBindGroupLayout);
    }

    /// <summary>The bind group made by the pixel-data constructor, for callers that pass a layout up front.</summary>
    public BindGroup* BindGroup { get; }

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

    /// <summary>The bind group binding this texture and its sampler for a pipeline using <paramref name="layout" />.</summary>
    public BindGroup* BindGroupFor(BindGroupLayout* layout)
    {
        if (_bindGroups.TryGetValue((nint)layout, out nint cached)) return (BindGroup*)cached;

        BindGroupEntry* entries = stackalloc BindGroupEntry[2];
        entries[0] = new BindGroupEntry { Binding = 0, TextureView = View };
        entries[1] = new BindGroupEntry { Binding = 1, Sampler = Sampler };

        BindGroupDescriptor bgDesc = new()
        {
            Layout = layout,
            EntryCount = 2,
            Entries = entries,
        };

        BindGroup* bindGroup = _device.Api.DeviceCreateBindGroup(_device.Device, in bgDesc);
        _bindGroups[(nint)layout] = (nint)bindGroup;
        return bindGroup;
    }

    /// <summary>Writes a rectangle of one mip level.</summary>
    public void WriteLevel(uint level, uint x, uint y, uint width, uint height, ReadOnlySpan<byte> rgba)
    {
        if (width == 0 || height == 0) return;

        ReadOnlySpan<byte> rows = WgpuPixelRows.Align(rgba, width, height, out uint bytesPerRow);
        Write(level, x, y, width, height, rows, bytesPerRow);
    }

    private void Write(uint level, uint x, uint y, uint width, uint height,
        ReadOnlySpan<byte> rgba, uint bytesPerRow)
    {
        Silk.NET.WebGPU.WebGPU api = _device.Api;

        ImageCopyTexture destination = new()
        {
            Texture = Texture,
            MipLevel = level,
            Origin = new Origin3D(x, y, 0),
            Aspect = TextureAspect.All,
        };

        TextureDataLayout layout = new()
        {
            Offset = 0,
            BytesPerRow = bytesPerRow,
            RowsPerImage = height,
        };

        Extent3D extent = new(width, height, 1);

        fixed (byte* p = rgba)
        {
            api.QueueWriteTexture(_device.Queue, in destination, p, (nuint)rgba.Length, in layout, in extent);
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
            MaxAnisotropy = (ushort)Math.Max(1u, description.MaxAnisotropy),
        };

        return _device.Api.DeviceCreateSampler(_device.Device, in samplerDesc);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Deferred like the sampler swap, and for the same reason: a texture pack reload disposes
        // every texture it is replacing from inside the frame that is drawing the options screen.
        WgpuRelease.Deferred(_device, [.. _bindGroups.Values], (nint)Sampler, (nint)View, (nint)Texture);
        _bindGroups.Clear();
    }
}
