using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.New.Authentication.Extensions;

namespace BetaSharp.Launcher.Features.New.Authentication;

internal sealed class MinecraftService(IHttpClientFactory httpClientFactory)
{
    public async Task<bool> GetGameAsync(string token)
    {
        var client = httpClientFactory.CreateClient();

        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var stream = await client.GetStreamAsync("https://api.minecraftservices.com/entitlements/mcstore");

        var node = await JsonNode.ParseAsync(stream);
        var items = node?["items"]?.AsArray() ?? [];

        return items.Any(item => item?["name"]?.GetValue<string>() is "game_minecraft" or "product_minecraft");
    }

    public async Task<string> GetNameAsync(string token)
    {
        var client = httpClientFactory.CreateClient();

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
        var request = new
        {
            identityToken = $"XBL3.0 x={hash};{token}"
        };

        var client = httpClientFactory.CreateClient();
        var response = await client.PostAsync("https://api.minecraftservices.com/authentication/login_with_xbox", request);

        response.EnsureSuccessStatusCode();

        return await response.Content.GetValueAsync("access_token");
    }
}