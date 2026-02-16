using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace BetaSharp.Launcher.Features.New;

internal sealed class DownloadingService(IHttpClientFactory httpClientFactory)
{
    public async Task DownloadAsync()
    {
        var client = httpClientFactory.CreateClient();

        await using var stream = await client.GetStreamAsync("https://launcher.mojang.com/v1/objects/43db9b498cb67058d2e12d394e6507722e71bb45/client.jar");
        await using var file = File.OpenWrite("b1.7.3.jar");

        await stream.CopyToAsync(file);
    }
}
