using System.Buffers;
using Microsoft.Extensions.Logging;
using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering.Core.Textures.Atlas;
using OmniBlock.Client.Resource.Pack;
using OmniBlock.Textures;
using Silk.NET.OpenGL;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using static OmniBlock.Client.Rendering.Core.Textures.TextureAtlasMipmapGenerator;

namespace OmniBlock.Client.Rendering.Core.Textures;

public class TextureManager : IDisposable
{
    private readonly Dictionary<string, int> _atlasTileSizes = [];
    private readonly Dictionary<string, int[]> _colors = [];
    private readonly List<DynamicTexture> _dynamicTextures = [];
    private readonly OmniBlock _game;
    private readonly GameOptions _gameOptions;
    private readonly Dictionary<uint, (Image<Rgba32> Image, TextureHandle Handle)> _images = [];
    private readonly ILogger _logger = Log.Instance.For<TextureManager>();
    private readonly Image<Rgba32> _missingTextureImage = new(256, 256);
    private readonly TexturePacks _texturePacks;
    private readonly Dictionary<string, TextureHandle> _textures = [];
    private bool _blur;
    private bool _clamp;
    private NamedTextureArray? _itemsArray;
    private TextureHandle? _itemsHandle;
    private NamedTextureArray? _terrainArray;
    private TextureHandle? _terrainHandle;

    public TextureManager(OmniBlock game, TexturePacks texturePacks, GameOptions options)
    {
        _game = game;
        _texturePacks = texturePacks;
        _gameOptions = options;
        _missingTextureImage.Mutate(ctx =>
        {
            ctx.BackgroundColor(Color.Magenta);
            ctx.Fill(Color.Black, new RectangleF(0, 0, 128, 128));
            ctx.Fill(Color.Black, new RectangleF(128, 128, 128, 128));
        });
    }

    /// <summary>The terrain tiles as a texture array addressed by name, built on first use.</summary>
    /// <remarks>
    ///     Built lazily rather than in the constructor because <see cref="NamedTextureArray.Rebuild" />
    ///     uploads to the GPU, and a <see cref="TextureManager" /> is constructed before there is a
    ///     context to upload into.
    /// </remarks>
    public NamedTextureArray TerrainArray => _terrainArray ??= BuildArray("terrain", Atlases.Terrain, "/terrain.png");

    /// <summary>The item icons as a texture array addressed by name, built on first use.</summary>
    public NamedTextureArray ItemsArray => _itemsArray ??= BuildArray("items", Atlases.Items, "/gui/items.png");

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        foreach (var handle in _textures.Values)
        {
            handle.Texture?.Dispose();
        }

        _textures.Clear();

        foreach (var entry in _images.Values)
        {
            entry.Handle.Texture?.Dispose();
            entry.Image.Dispose();
        }

        _images.Clear();

        _terrainArray?.Dispose();
        _itemsArray?.Dispose();

        _missingTextureImage.Dispose();
        _colors.Clear();
        _dynamicTextures.Clear();
    }

    private NamedTextureArray BuildArray(string domain, AtlasTileMap tileMap, string defaultGridPath)
    {
        NamedTextureArray array = new(
            domain,
            tileMap,
            () => LoadImageFromResource(defaultGridPath),
            () => _texturePacks.SelectedTexturePack);

        array.Rebuild();
        return array;
    }

    public int[] GetColors(string path)
    {
        if (_colors.TryGetValue(path, out var cachedColors)) return cachedColors;
        try
        {
            using var img = LoadImageFromResource(path);
            var result = ReadColorsFromImage(img);
            _colors[path] = result;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get colors from image {Path}", path);
            var fallback = ReadColorsFromImage(_missingTextureImage);
            _colors[path] = fallback;
            return fallback;
        }
    }

    public int GetAtlasTileSize(string path)
    {
        if (_atlasTileSizes.TryGetValue(path, out var size)) return size;
        return 16;
    }

    public TextureHandle Load(Image<Rgba32> image)
    {
        var texture = new Texture2D("Image_Direct");
        Load(image, texture, false);
        var handle = new TextureHandle(texture);
        _images[texture.Id] = (image, handle);
        return handle;
    }

    public TextureHandle GetTextureId(string path)
    {
        if (_textures.TryGetValue(path, out var handle)) return handle;

        var texture = new Texture2D(path);
        handle = new TextureHandle(texture);
        _textures[path] = handle;

        try
        {
            using var img = LoadImageFromResource(path);

            _atlasTileSizes[path] = img.Width / 16;

            Load(img, texture, path.Contains("terrain.png"));
            return handle;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get texture id for path {Path}", path);
            Load(_missingTextureImage, texture, false);
            return handle;
        }
    }

    public unsafe void Load(Image<Rgba32> image, Texture2D texture, bool isTerrain)
    {
        texture.Bind();

        if (isTerrain)
        {
            var tileSize = image.Width / 16;
            var mips = GenerateMipmaps(image, tileSize);
            var mipCount = _gameOptions.UseMipmaps ? mips.Length : 1;

            for (var level = 0; level < mipCount; level++)
            {
                var mip = mips[level];
                var pixels = new byte[mip.Width * mip.Height * 4];
                mip.CopyPixelDataTo(pixels);
                fixed (byte* ptr = pixels)
                {
                    texture.Upload(mip.Width, mip.Height, ptr, level, PixelFormat.Rgba, InternalFormat.Rgba8);
                }

                if (level > 0) mip.Dispose();
            }

            texture.SetFilter(_gameOptions.UseMipmaps ? TextureMinFilter.NearestMipmapNearest : TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            texture.SetMaxLevel(mipCount - 1);

            var aniso = _gameOptions.AnisotropicLevel == 0 ? 1.0f : (float)Math.Pow(2, _gameOptions.AnisotropicLevel);
            aniso = Math.Clamp(aniso, 1.0f, GameOptions.MaxAnisotropy);

            texture.SetAnisotropicFilter(aniso);

            return;
        }

        texture.SetFilter(_blur ? TextureMinFilter.Linear : TextureMinFilter.Nearest, _blur ? TextureMagFilter.Linear : TextureMagFilter.Nearest);
        texture.SetWrap(_clamp ? TextureWrapMode.ClampToEdge : TextureWrapMode.Repeat, _clamp ? TextureWrapMode.ClampToEdge : TextureWrapMode.Repeat);

        var rawPixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(rawPixels);
        fixed (byte* ptr = rawPixels)
        {
            texture.Upload(image.Width, image.Height, ptr);
        }

        _clamp = false;
        _blur = false;
    }

    public void BindTexture(TextureHandle? handle) => handle?.Bind();

    private Image<Rgba32> Rescale(Image<Rgba32> image)
    {
        var scale = image.Width / 16;
        var rescaled = new Image<Rgba32>(16, image.Height * scale);
        rescaled.Mutate(ctx =>
        {
            for (var i = 0; i < scale; i++)
            {
                using var frame = image.Clone(x => x.Crop(new Rectangle(i * 16, 0, 16, image.Height)));
                ctx.DrawImage(frame, new Point(0, i * image.Height), 1f);
            }
        });
        return rescaled;
    }

    private int[] ReadColorsFromImage(Image<Rgba32> image)
    {
        var argb = new int[image.Width * image.Height];
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < accessor.Width; x++)
                {
                    var p = row[x];
                    argb[y * accessor.Width + x] = (p.A << 24) | (p.R << 16) | (p.G << 8) | p.B;
                }
            }
        });
        return argb;
    }


    private Image<Rgba32> LoadImageFromResource(string path)
    {
        var pack = _texturePacks.SelectedTexturePack;

        if (path.StartsWith("##"))
        {
            using var s = pack.GetResourceAsStream(path[2..]);
            return s == null ? _missingTextureImage.Clone() : Rescale(Image.Load<Rgba32>(s));
        }

        var cleanPath = path;
        while (true)
        {
            if (cleanPath.StartsWith("%clamp%"))
            {
                _clamp = true;
                cleanPath = cleanPath[7..];
            }
            else if (cleanPath.StartsWith("%blur%"))
            {
                _blur = true;
                cleanPath = cleanPath[6..];
            }
            else break;
        }

        using var stream = pack.GetResourceAsStream(cleanPath);
        var img = stream == null ? _missingTextureImage.Clone() : Image.Load<Rgba32>(stream);

        return img;
    }


    public unsafe void Bind(int[] packedARGB, int width, int height, Texture2D texture)
    {
        //TODO: this is potentially wrong but shouldn't crash

        texture.Bind();

        texture.SetFilter(_blur ? TextureMinFilter.Linear : TextureMinFilter.Nearest, _blur ? TextureMagFilter.Linear : TextureMagFilter.Nearest);
        texture.SetWrap(_clamp ? TextureWrapMode.ClampToEdge : TextureWrapMode.Repeat, _clamp ? TextureWrapMode.ClampToEdge : TextureWrapMode.Repeat);

        var unpackedRGBA = new byte[width * height * 4];

        for (var i = 0; i < packedARGB.Length; ++i)
        {
            var a = (packedARGB[i] >> 24) & 255;
            var r = (packedARGB[i] >> 16) & 255;
            var g = (packedARGB[i] >> 8) & 255;
            var b = packedARGB[i] & 255;

            unpackedRGBA[i * 4 + 0] = (byte)r;
            unpackedRGBA[i * 4 + 1] = (byte)g;
            unpackedRGBA[i * 4 + 2] = (byte)b;
            unpackedRGBA[i * 4 + 3] = (byte)a;
        }

        fixed (byte* ptr = unpackedRGBA)
        {
            texture.UploadSubImage(0, 0, width, height, ptr);
        }
    }

    public void Delete(Texture2D texture)
    {
        var textureEntry = _textures.FirstOrDefault(x => x.Value.Texture == texture);
        if (textureEntry.Key != null) _textures.Remove(textureEntry.Key);

        _images.Remove(texture.Id);
        texture.Dispose();
    }

    public void Delete(TextureHandle handle)
    {
        if (handle.Texture != null) Delete(handle.Texture);
    }


    public void AddDynamicTexture(DynamicTexture t)
    {
        _dynamicTextures.Add(t);
        t.Setup(_game);
        t.tick();

        _terrainHandle = null;
        _itemsHandle = null;
    }

    public void Reload()
    {
        _atlasTileSizes.Clear();
        foreach (var entry in _textures)
        {
            entry.Value.Texture?.Dispose();

            var newTexture = new Texture2D(entry.Key);
            entry.Value.Texture = newTexture;

            try
            {
                using var img = LoadImageFromResource(entry.Key);
                _atlasTileSizes[entry.Key] = img.Width / 16;
                Load(img, newTexture, entry.Key.Contains("terrain.png"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reload texture {Path}", entry.Key);
                _atlasTileSizes[entry.Key] = _missingTextureImage.Width / 16;
                Load(_missingTextureImage, newTexture, false);
            }
        }

        var oldImages = new Dictionary<uint, (Image<Rgba32> Image, TextureHandle Handle)>(_images);
        _images.Clear();
        foreach (var entry in oldImages)
        {
            entry.Value.Handle.Texture?.Dispose();

            var newTexture = new Texture2D(entry.Value.Handle.Texture?.Source ?? "Image_Direct_Reload");
            entry.Value.Handle.Texture = newTexture;
            Load(entry.Value.Image, newTexture, false);
            _images[newTexture.Id] = entry.Value;
        }

        foreach (var key in new List<string>(_colors.Keys)) GetColors(key);

        foreach (var dynamicTexture in _dynamicTextures)
        {
            dynamicTexture.Setup(_game);
        }

        // Re-resolves every name through the new pack. Only the arrays that were already built get
        // one: a pack switch is no reason to pay for an array nothing has asked for yet.
        _terrainArray?.Rebuild();
        _itemsArray?.Rebuild();

        _terrainHandle = null;
        _itemsHandle = null;
    }

    public unsafe void Tick()
    {
        _terrainHandle ??= _textures.FirstOrDefault(x => x.Key.EndsWith("/terrain.png")).Value
                           ?? GetTextureId("/terrain.png");
        _itemsHandle ??= _textures.FirstOrDefault(x => x.Key.EndsWith("/gui/items.png")).Value
                         ?? GetTextureId("/gui/items.png");

        foreach (var texture in _dynamicTextures)
        {
            texture.tick();

            var atlasHandle = texture.Atlas == DynamicTexture.FxImage.Terrain
                ? _terrainHandle
                : _itemsHandle;

            var atlasTexture = atlasHandle?.Texture;
            if (atlasTexture == null) continue;

            var targetTileSize = atlasTexture.Width / 16;

            var tileX = texture.Sprite % 16 * targetTileSize;
            var tileY = texture.Sprite / 16 * targetTileSize;

            var fxSize = (int)Math.Sqrt(texture.Pixels.Length / 4);
            var scale = targetTileSize / fxSize;
            if (scale < 1) scale = 1;

            var uploadPixels = texture.Pixels;
            var uploadSize = fxSize;
            byte[]? rentedArray = null;

            try
            {
                if (scale > 1)
                {
                    uploadSize = fxSize * scale;
                    rentedArray = ArrayPool<byte>.Shared.Rent(uploadSize * uploadSize * 4);
                    UpscaleNearestNeighbor(texture.Pixels, rentedArray, fxSize, uploadSize, scale);
                    uploadPixels = rentedArray;
                }

                var finalReplicate = texture.Replicate;

                fixed (byte* ptr = uploadPixels)
                {
                    for (var x = 0; x < finalReplicate; x++)
                    {
                        for (var y = 0; y < finalReplicate; y++)
                        {
                            atlasTexture.UploadSubImage(
                                tileX + x * uploadSize,
                                tileY + y * uploadSize,
                                uploadSize, uploadSize, ptr);
                        }
                    }
                }

                if (texture.Atlas == DynamicTexture.FxImage.Terrain && _gameOptions.UseMipmaps)
                {
                    for (var x = 0; x < finalReplicate; x++)
                    {
                        for (var y = 0; y < finalReplicate; y++)
                        {
                            UpdateTileMipmaps(tileX + x * uploadSize, tileY + y * uploadSize, uploadSize, targetTileSize, uploadPixels, atlasTexture);
                        }
                    }
                }

                UploadAnimatedLayer(texture, fxSize);
            }
            finally
            {
                if (rentedArray != null)
                {
                    ArrayPool<byte>.Shared.Return(rentedArray);
                }
            }
        }
    }

    /// <summary>
    ///     Writes an animated tile's new frame into its layer of the named array, alongside the 2D
    ///     atlas the same frame just went into.
    /// </summary>
    /// <remarks>
    ///     No replication here, unlike the atlas: Beta wrote flowing water into a 2x2 block of cells
    ///     so a quad sweeping past a cell edge landed on more water, and a layer wrapping onto itself
    ///     does that on its own.
    /// </remarks>
    private unsafe void UploadAnimatedLayer(DynamicTexture texture, int fxSize)
    {
        var isTerrain = texture.Atlas == DynamicTexture.FxImage.Terrain;
        var array = isTerrain ? TerrainArray : ItemsArray;
        var tileMap = isTerrain ? Atlases.Terrain : Atlases.Items;

        var layer = tileMap.LayerOfGridIndex(texture.Sprite);
        if (layer == AtlasTileMap.MissingLayer || array.Texture == null) return;

        var scale = Math.Max(1, array.LayerSize / fxSize);
        var size = fxSize * scale;

        byte[]? rented = null;
        var pixels = texture.Pixels;

        try
        {
            if (scale > 1)
            {
                rented = ArrayPool<byte>.Shared.Rent(size * size * 4);
                UpscaleNearestNeighbor(texture.Pixels, rented, fxSize, size, scale);
                pixels = rented;
            }

            fixed (byte* ptr = pixels)
            {
                array.Texture.UploadLayer(layer, size, size, ptr);
            }
        }
        finally
        {
            if (rented != null) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void UpscaleNearestNeighbor(byte[] src, byte[] dst, int srcSize, int dstSize, int scale)
    {
        ReadOnlySpan<byte> srcSpan = src;
        Span<byte> dstSpan = dst;

        for (var y = 0; y < dstSize; y++)
        {
            var srcY = y / scale;
            for (var x = 0; x < dstSize; x++)
            {
                var srcX = x / scale;
                var srcIdx = (srcY * srcSize + srcX) * 4;
                var dstIdx = (y * dstSize + x) * 4;

                dstSpan[dstIdx] = srcSpan[srcIdx];
                dstSpan[dstIdx + 1] = srcSpan[srcIdx + 1];
                dstSpan[dstIdx + 2] = srcSpan[srcIdx + 2];
                dstSpan[dstIdx + 3] = srcSpan[srcIdx + 3];
            }
        }
    }

    private unsafe void UpdateTileMipmaps(int baseX, int baseY, int dataSize, int targetTileSize, byte[] tileData, Texture2D texture)
    {
        var maxMipLevels = (int)Math.Log2(targetTileSize) + 1;
        var currentData = tileData;
        var currentSize = dataSize;

        for (var mipLevel = 1; mipLevel < maxMipLevels; mipLevel++)
        {
            var newSize = currentSize >> 1;
            if (newSize < 1) newSize = 1;

            var downsampled = ArrayPool<byte>.Shared.Rent(newSize * newSize * 4);

            try
            {
                if (currentSize > 1)
                {
                    for (var y = 0; y < newSize; y++)
                    {
                        for (var x = 0; x < newSize; x++)
                        {
                            var src0 = (y * 2 * currentSize + x * 2) * 4;
                            var src1 = (y * 2 * currentSize + x * 2 + 1) * 4;
                            var src2 = ((y * 2 + 1) * currentSize + x * 2) * 4;
                            var src3 = ((y * 2 + 1) * currentSize + x * 2 + 1) * 4;

                            var dst = (y * newSize + x) * 4;

                            downsampled[dst] = (byte)((currentData[src0] + currentData[src1] + currentData[src2] + currentData[src3]) >> 2);
                            downsampled[dst + 1] = (byte)((currentData[src0 + 1] + currentData[src1 + 1] + currentData[src2 + 1] + currentData[src3 + 1]) >> 2);
                            downsampled[dst + 2] = (byte)((currentData[src0 + 2] + currentData[src1 + 2] + currentData[src2 + 2] + currentData[src3 + 2]) >> 2);
                            downsampled[dst + 3] = (byte)((currentData[src0 + 3] + currentData[src1 + 3] + currentData[src2 + 3] + currentData[src3 + 3]) >> 2);
                        }
                    }
                }
                else
                {
                    for (var i = 0; i < 4; i++) downsampled[i] = currentData[i];
                }

                var mipX = baseX >> mipLevel;
                var mipY = baseY >> mipLevel;

                fixed (byte* ptr = downsampled)
                {
                    texture.UploadSubImage(mipX, mipY, newSize, newSize, ptr, mipLevel);
                }

                if (mipLevel > 1)
                {
                    ArrayPool<byte>.Shared.Return(currentData);
                }

                currentData = downsampled;
                currentSize = newSize;
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(downsampled);
                throw;
            }
        }

        if (currentData != tileData)
        {
            ArrayPool<byte>.Shared.Return(currentData);
        }
    }
}
