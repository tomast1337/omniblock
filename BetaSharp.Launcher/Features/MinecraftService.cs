using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Extensions;

namespace BetaSharp.Launcher.Features;

internal sealed class MinecraftService(HttpClient client)
{
    public async Task<string> GetNameAsync(string token)
    {
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        await using var stream = await client.GetStreamAsync("https://api.minecraftservices.com/minecraft/profile");

        var node = await JsonNode.ParseAsync(stream);
        string? name = node?["name"]?.GetValue<string>();

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return name;
    }

    public async Task<string> GetTokenAsync(string token, string hash)
    {
        var request = new { identityToken = $"XBL3.0 x={hash};{token}" };
        var response = await client.PostAsync("https://api.minecraftservices.com/authentication/login_with_xbox", request);

        response.EnsureSuccessStatusCode();

        return await response.Content.GetValueAsync("access_token");
    }
}
