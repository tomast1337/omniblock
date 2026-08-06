using Silk.NET.WebGPU;

namespace BetaSharp.Client.Rendering.Core.WebGPU;

/// <summary>
///     A 2D texture, its view and a sampler, with the bind group that binds them to the slot the
///     textured shaders expect (group 1).
/// </summary>
public sealed unsafe class WgpuTexture : IDisposable
{
    public Texture* Texture { get; }
    public TextureView* View { get; }
    public Sampler* Sampler { get; }

    /// <summary>The bind group at group 1 binding the 2D texture and sampler.</summary>
    public BindGroup* BindGroup { get; }

    /// <summary>The format every texture in this path is uploaded as.</summary>
    public const TextureFormat Format = TextureFormat.Rgba8Unorm;

    private readonly WebGpuDevice _device;
    private bool _disposed;

    /// <summary>Creates a 2D texture from RGBA8 pixel data.</summary>
    public WgpuTexture(WebGpuDevice device, uint width, uint height, ReadOnlySpan<byte> rgba,
        BindGroupLayout* textureBindGroupLayout)
    {
        _device = device;
        Silk.NET.WebGPU.WebGPU api = device.Api;

        // Texture.
        TextureDescriptor texDesc = new()
        {
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D(width, height, 1),
            Format = Format,
            MipLevelCount = 1,
            SampleCount = 1,
        };

        Texture = api.DeviceCreateTexture(device.Device, in texDesc);

        // View.
        TextureViewDescriptor viewDesc = new()
        {
            Format = Format,
            Dimension = TextureViewDimension.Dimension2D,
            MipLevelCount = 1,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All,
        };

        View = api.TextureCreateView(Texture, in viewDesc);

        // Upload.
        ImageCopyTexture destination = new()
        {
            Texture = Texture,
            MipLevel = 0,
            Origin = default,
            Aspect = TextureAspect.All,
        };

        TextureDataLayout layout = new()
        {
            Offset = 0,
            BytesPerRow = width * 4,
            RowsPerImage = height,
        };

        Extent3D extent = new(width, height, 1);

        fixed (byte* p = rgba)
        {
            api.QueueWriteTexture(device.Queue, in destination, p, (nuint)rgba.Length, in layout, in extent);
        }

        // Sampler — repeat, for the flowing-water rotation that relies on wrapping.
        SamplerDescriptor samplerDesc = new()
        {
            AddressModeU = AddressMode.Repeat,
            AddressModeV = AddressMode.Repeat,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Linear,
            LodMinClamp = 0.0f,
            LodMaxClamp = 1.0f,
            MaxAnisotropy = 1,
        };

        Sampler = api.DeviceCreateSampler(device.Device, in samplerDesc);

        // Bind group — 2 entries (2D texture + sampler) matching the shader.
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Silk.NET.WebGPU.WebGPU api = _device.Api;
        if (BindGroup is not null) api.BindGroupRelease(BindGroup);
        if (Sampler is not null) api.SamplerRelease(Sampler);
        if (View is not null) api.TextureViewRelease(View);
        if (Texture is not null)
        {
            api.TextureDestroy(Texture);
            api.TextureRelease(Texture);
        }
    }
}
