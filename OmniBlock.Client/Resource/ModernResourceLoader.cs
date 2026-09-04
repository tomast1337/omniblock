using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OmniBlock.Client.Resource;

public class ModernAssetDownloader : IResourceLoader, IDisposable
{
    private const string ASSET_INDEX_URL = "https://piston-meta.mojang.com/v1/packages/fb03309bb7711f3f9b5d44eea061e27222edc2d2/26.json";
    private const string ASSET_BASE_URL = "https://resources.download.minecraft.net/";
    private const string OUTPUT_FOLDER = "custom";

    private static readonly Dictionary<string, string> ExtensionToFolder = new()
    {
        { ".ogg", "music" }
    };

    private readonly OmniBlock _game;
    private readonly HttpClient _httpClient;

    private readonly ILogger<ModernAssetDownloader> _logger = Log.Instance.For<ModernAssetDownloader>();
    private readonly string _resourcesDirectory;
    private readonly IEnumerable<string> _wantedAssets;
    private bool _cancelled;

    public ModernAssetDownloader(OmniBlock game, string baseDirectory, IEnumerable<string> wantedAssets)
    {
        _wantedAssets = wantedAssets;
        _game = game;
        _resourcesDirectory = Path.Combine(baseDirectory, "resources");
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient?.Dispose();
    }

    public async Task LoadAsync() => await DownloadAssetsAsync(_wantedAssets);

    public async Task DownloadAssetsAsync(IEnumerable<string> wantedAssets)
    {
        try
        {
            var response = await _httpClient.GetAsync(ASSET_INDEX_URL);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var index = ParseAssetIndex(json);

            foreach (var assetName in wantedAssets)
            {
                if (_cancelled) return;

                if (!index.TryGetValue(assetName, out var entry))
                {
                    continue;
                }

                var fileName = Path.GetFileName(assetName);
                var extension = Path.GetExtension(assetName).ToLowerInvariant();

                if (!ExtensionToFolder.TryGetValue(extension, out var subFolder))
                {
                    _logger.LogError($"No folder mapping for extension {extension}, skipping {fileName}");
                    continue;
                }

                var outputKey = Path.Combine(OUTPUT_FOLDER, subFolder, fileName).Replace('\\', '/');
                var localFile = new FileInfo(Path.Combine(_resourcesDirectory, OUTPUT_FOLDER, subFolder, fileName));

                if (localFile.Exists && localFile.Length == entry.Size)
                {
                    _game.InstallResource(outputKey, localFile);
                    continue;
                }

                localFile.Directory?.Create();

                var hash = entry.Hash;
                var url = ASSET_BASE_URL + hash[..2] + "/" + hash;

                await DownloadFile(url, localFile.FullName);

                if (!_cancelled)
                {
                    _game.InstallResource(outputKey, localFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error downloading modern assets: {ex}");
        }
    }

    private static Dictionary<string, AssetEntry> ParseAssetIndex(string json)
    {
        var result = new Dictionary<string, AssetEntry>();
        using var doc = JsonDocument.Parse(json);

        var objects = doc.RootElement.GetProperty("objects");

        foreach (var prop in objects.EnumerateObject())
        {
            var hash = prop.Value.GetProperty("hash").GetString()!;
            var size = prop.Value.GetProperty("size").GetInt64();
            result[prop.Name] = new AssetEntry
            {
                Hash = hash,
                Size = size
            };
        }

        return result;
    }

    private async Task DownloadFile(string url, string destinationPath)
    {
        var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[4096];
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
        {
            if (_cancelled) return;
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
        }
    }

    public void Cancel() => _cancelled = true;

    private class AssetEntry
    {
        public string Hash { get; set; } = "";
        public long Size { get; set; }
    }
}
