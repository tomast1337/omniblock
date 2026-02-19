using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BetaSharp.Launcher.Features;

internal static class HttpClientExtensions
{
    public static async Task<TResponse> PostAsync<TRequest, TResponse>(this HttpClient client, string uri, TRequest instance)
    {
        var request = await client.PostAsync(uri, new StringContent(
            JsonSerializer.Serialize(instance, SourceGenerationContext.Default.Options),
            Encoding.UTF8,
            "application/json"));

        var response = await request.Content.ReadFromJsonAsync<TResponse>(SourceGenerationContext.Default.Options);

        ArgumentNullException.ThrowIfNull(response);

        return response;
    }
}
