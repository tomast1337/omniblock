using Microsoft.Extensions.Logging;
using OmniBlock.Client.Rendering.Blocks.Models;
using OmniBlock.Client.Rendering.Core.Textures.Atlas;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Client.Resource.Pack;
using OmniBlock.Textures;
using OmniBlock.Worlds.ClientData.Colors;
using OmniBlock.Worlds.Colors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OmniBlock.Client.Rendering.Core.Textures;

public partial class TextureManager
{
    private readonly int _reloadThread = Environment.CurrentManagedThreadId;
    internal BlockModelBindings? BlockBindings { get; private set; }

    /// <summary>
    /// Synchronous render-thread transaction. All fallible reads/uploads precede the optional
    /// persistence callback; commit only exchanges prepared references. No render callbacks run
    /// during commit. GPU wrappers defer native retirement through the existing submit lifetime.
    /// </summary>
    internal bool TryReload(TexturePack pack, Func<bool>? persistSelection = null,
        HashSet<string>? captureDependencies = null)
    {
        if (Environment.CurrentManagedThreadId != _reloadThread)
            throw new InvalidOperationException("Texture reload requires the owning render thread.");
        var dependencies = captureDependencies ?? _impostorCaptureDependencies;
        try
        {
            if (WebGpuGameRenderer.IsRecordingFrame)
                throw new InvalidOperationException("Texture reload must run between render frames.");
            using var candidate = PrepareReload(pack, dependencies);
            BlockModels.ValidateCandidate(candidate.Models!);
            var retiredTextures = candidate.Textures.Keys.Select(key => _textures[key].Texture).ToArray();
            var oldTerrain = _terrainArray;
            var oldItems = _itemsArray;
            // Saving is the last fallible operation. If it fails, no live resource changes.
            if (persistSelection?.Invoke() == false) return false;

            BlockModels.Replace(() => candidate.Models!);
            BlockBindings = candidate.Bindings;
            foreach (var (key, texture) in candidate.Textures) _textures[key].Texture = texture;
            _atlasTileSizes = candidate.TileSizes;
            _colors = candidate.Colors;
            _dynamicTextures = candidate.Animations;
            _terrainArray = candidate.Terrain;
            _itemsArray = candidate.Items;
            _impostorCaptureDependencies = dependencies;
            WaterColors.loadColors(_colors["/misc/watercolor.png"]);
            GrassColors.loadColors(_colors["/misc/grasscolor.png"]);
            FoliageColors.loadColors(_colors["/misc/foliagecolor.png"]);
            _terrainHandle = null;
            _itemsHandle = null;
            ResourceGeneration++;
            candidate.Installed = true;

            foreach (var texture in retiredTextures) texture?.Dispose();
            oldTerrain?.Dispose();
            oldItems?.Dispose();
            // Tints are baked into existing meshes. Invalidate after successful publication only.
            try
            {
                if (_game.World != null) _game.WorldRenderer.ChunkRenderer.MarkAllVisibleChunksDirty();
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Resources reloaded, but terrain invalidation failed."); }
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Texture pack reload rejected; candidate resources were discarded.");
            return false;
        }
    }

    private ReloadCandidate PrepareReload(TexturePack pack, HashSet<string> dependencies)
    {
        var candidate = new ReloadCandidate();
        try
        {
            using var source = new TexturePackSnapshot(pack);
            var images = new ReloadImageReader(source.Open);
            var overrides = new ReloadImageReader(source.OpenOverride);
            using (var animations = new TextureAnimationContext(source.Open))
                candidate.Animations = DynamicTexture.PrepareReload(_dynamicTextures, animations);

            foreach (var path in _colors.Keys.Concat(["/misc/watercolor.png", "/misc/grasscolor.png", "/misc/foliagecolor.png"]).Distinct(StringComparer.Ordinal))
            {
                using var image = images.Read(path) ?? throw new InvalidDataException($"Missing color map '{path}'.");
                ReloadImageReader.ValidateColors(image, path);
                candidate.Colors.Add(path, ReadColorsFromImage(image));
            }

            using var modelSource = new BlockModelPackResources(Atlases.Terrain, source.OpenOverride,
                TexturePackSnapshot.OpenBuiltin, () =>
                {
                    using var stream = TexturePackSnapshot.OpenBuiltin("terrain.png")
                        ?? throw new InvalidDataException("Missing builtin terrain atlas.");
                    return Image.Load<Rgba32>(stream);
                });
            var fixedLayers = modelSource.FixedLayers();
            var states = BlockStateDefinitions.Load(source.OpenOverride, TexturePackSnapshot.OpenBuiltin,
                _game.Content.BlockStateProperties);
            var fence = FencePartDefinitions.Load(source.OpenOverride, TexturePackSnapshot.OpenBuiltin);
            var models = PreparedBlockModelResources.Build(states.ModelRoots.Append(fence.Model), fixedLayers,
                modelSource.OpenOverride, modelSource.OpenBuiltin);
            candidate.Bindings = BlockModelBindings.Build(ResourceGeneration + 1, _game.Content.Blocks, models.Models,
                fixedLayers, states, fence);
            var device = WebGpuDevice.Current ?? throw new InvalidOperationException("Texture reload requires a WebGPU device.");
            var errors = device.ErrorCount;
            foreach (var path in _textures.Keys)
            {
                var cleanPath = path;
                var clamp = false;
                var blur = false;
                var rescale = cleanPath.StartsWith("##", StringComparison.Ordinal);
                if (rescale) cleanPath = cleanPath[2..];
                while (true)
                {
                    if (cleanPath.StartsWith("%clamp%", StringComparison.Ordinal)) { clamp = true; cleanPath = cleanPath[7..]; }
                    else if (cleanPath.StartsWith("%blur%", StringComparison.Ordinal)) { blur = true; cleanPath = cleanPath[6..]; }
                    else break;
                }
                using var original = images.Read(cleanPath) ?? _missingTextureImage.Clone();
                if (rescale && original.Width % 16 != 0) throw new InvalidDataException($"Texture '{path}': invalid strip width.");
                using var scaled = rescale ? Rescale(original) : null;
                var image = scaled ?? original;
                var terrain = cleanPath.TrimStart('/') == "terrain.png";
                if (terrain) ReloadImageReader.ValidateGrid(image, cleanPath);
                var texture = new Texture2D(path, dependencies.Contains(path));
                candidate.Textures.Add(path, texture);
                UploadImage(image, texture, terrain, blur, clamp);
                candidate.TileSizes.Add(path, image.Width / 16);
            }

            candidate.Models = models.Upload(_gameOptions.UseMipmaps);
            candidate.Terrain = new NamedTextureArray("terrain", Atlases.Terrain,
                () => throw new InvalidOperationException("Prepared terrain must not be rebuilt."), () => pack);
            candidate.Terrain.UsePreparedTexture(candidate.Models.Texture, models.LayerSize,
                models.Sources.ToDictionary(pair => pair.Key.Path, pair => pair.Value, StringComparer.Ordinal));
            candidate.Items = BuildCandidateArray("items", Atlases.Items, "gui/items.png");
            device.Poll();
            if (device.ErrorCount != errors || candidate.Textures.Values.Any(t => t.Wgpu is null))
                throw new InvalidOperationException("WebGPU rejected the resource candidate.");
            return candidate;

            NamedTextureArray BuildCandidateArray(string domain, AtlasTileMap map, string grid)
            {
                var array = new NamedTextureArray(domain, map, () =>
                {
                    var image = images.Read(grid) ?? throw new InvalidDataException($"Missing atlas '{grid}'.");
                    try { ReloadImageReader.ValidateGrid(image, grid); return image; }
                    catch { image.Dispose(); throw; }
                }, () => pack, () => _gameOptions.UseMipmaps, overrides.Read);
                try { array.Rebuild(); array.Seal(); return array; }
                catch { array.Dispose(); throw; }
            }
        }
        catch { candidate.Dispose(); throw; }
    }

    private sealed class ReloadCandidate : IDisposable
    {
        public readonly Dictionary<string, Texture2D> Textures = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> TileSizes = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int[]> Colors = new(StringComparer.Ordinal);
        public List<DynamicTexture> Animations = [];
        public NamedTextureArray? Terrain, Items;
        public UploadedBlockModelResources? Models;
        public BlockModelBindings? Bindings;
        public bool Installed;

        public void Dispose()
        {
            if (Installed) return;
            foreach (var texture in Textures.Values) texture.Dispose();
            Terrain?.Dispose();
            Items?.Dispose();
            Models?.Dispose();
        }
    }
}
