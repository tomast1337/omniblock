using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BetaSharp.Launcher.Features.New.Authentication.Extensions;

internal static class HttpClientExtensions
{
    public static async Task<HttpResponseMessage> PostAsync<T>(this HttpClient client, string uri, T instance) where T : class
    {
        var content = new StringContent(JsonSerializer.Serialize(instance), Encoding.UTF8, "application/json");
        return await client.PostAsync(uri, content);
    }
}
