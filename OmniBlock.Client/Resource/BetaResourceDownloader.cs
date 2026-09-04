using System.Net;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace OmniBlock.Client.Resource;

public class BetaResourceDownloader : IResourceLoader, IDisposable
{
    private const string RESOURCE_URL = "http://s3.amazonaws.com/MinecraftResources/";
    private const string BETACRAFT_PROXY_HOST = "betacraft.uk";
    private const int BETACRAFT_PROXY_PORT = 11705;
    private readonly OmniBlock _game;
    private readonly HttpClient _httpClient;

    private readonly ILogger<BetaResourceDownloader> _logger = Log.Instance.For<BetaResourceDownloader>();
    private readonly string _resourcesDirectory;
    private bool _cancelled;

    public BetaResourceDownloader(OmniBlock game, string baseDirectory)
    {
        _game = game;
        _resourcesDirectory = Path.Combine(baseDirectory, "resources");
        Directory.CreateDirectory(_resourcesDirectory);

        var handler = new HttpClientHandler
        {
            Proxy = new WebProxy(BETACRAFT_PROXY_HOST, BETACRAFT_PROXY_PORT),
            UseProxy = true
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _httpClient?.Dispose();
    }

    public async Task LoadAsync()
    {
        var manifestFilePath = Path.Combine(_resourcesDirectory, "resourceManifest.txt");

        if (DoManifestStuff(manifestFilePath))
        {
            return;
        }

        try
        {
            _logger.LogInformation("Fetching resource list...");

            var response = await _httpClient.GetAsync(RESOURCE_URL);
            response.EnsureSuccessStatusCode();

            var xmlContent = await response.Content.ReadAsStringAsync();

            var resources = ParseResourceXml(xmlContent);

            List<string> resourceFileNames = [];

            foreach (var resource in resources)
            {
                resourceFileNames.Add(resource.Key);
            }

            File.WriteAllLines(manifestFilePath, resourceFileNames);

            _logger.LogInformation($"Found {resources.Count} resources to download");

            for (var pass = 0; pass < 2; pass++)
            {
                foreach (var resource in resources)
                {
                    if (_cancelled) return;

                    await LoadFromUrl(resource.Key, resource.Size, pass);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error downloading resources: {ex.Message}");
        }
    }

    private bool DoManifestStuff(string manifestFilePath)
    {
        if (File.Exists(manifestFilePath))
        {
            var lines = File.ReadAllLines(manifestFilePath);
            var loaded = 0;

            foreach (var line in lines)
            {
                var localFile = Path.Combine(_resourcesDirectory, line);
                if (File.Exists(localFile))
                {
                    loaded++;
                    _game.InstallResource(line, new FileInfo(localFile));
                }
            }

            if (lines.Length == loaded)
            {
                _logger.LogInformation($"{loaded} resources");
                return true;
            }

            _logger.LogError($"resource count mismatch, expected {lines.Length}, loaded {loaded}");
        }

        return false;
    }

    private static List<ResourceEntry> ParseResourceXml(string xmlContent)
    {
        var resources = new List<ResourceEntry>();
        var doc = new XmlDocument();
        doc.LoadXml(xmlContent);

        var contents = doc.GetElementsByTagName("Contents");

        foreach (XmlNode node in contents)
        {
            if (node.NodeType == XmlNodeType.Element)
            {
                var element = (XmlElement)node;

                var keyNode = element.GetElementsByTagName("Key")[0];
                var sizeNode = element.GetElementsByTagName("Size")[0];

                var key = keyNode!.InnerText;
                var size = long.Parse(sizeNode!.InnerText);

                if (size > 0)
                {
                    resources.Add(new ResourceEntry
                    {
                        Key = key,
                        Size = size
                    });
                }
            }
        }

        return resources;
    }

    private async Task LoadFromUrl(string path, long size, int pass)
    {
        try
        {
            var slashIndex = path.IndexOf('/');
            if (slashIndex < 0) return;

            var category = path.Substring(0, slashIndex);

            var isSoundFile = category == "sound" || category == "newsound";

            if (isSoundFile && pass != 0) return;
            if (!isSoundFile && pass != 1) return;

            var localFile = new FileInfo(Path.Combine(_resourcesDirectory, path));

            if (localFile.Exists && localFile.Length == size)
            {
                _game.InstallResource(path, new FileInfo(localFile.FullName));
                return;
            }

            localFile.Directory?.Create();

            var urlPath = path.Replace(" ", "%20");
            var fullUrl = RESOURCE_URL + urlPath;

            await DownloadFile(fullUrl, localFile.FullName);

            if (!_cancelled)
            {
                _game.InstallResource(path, new FileInfo(localFile.FullName));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to download {path}: {ex.Message}");
        }
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

    private class ResourceEntry
    {
        public string Key { get; set; }
        public long Size { get; set; }
    }
}
