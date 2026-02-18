using System;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using BetaSharp.Launcher.Features.Extensions;

namespace BetaSharp.Launcher.Features;

internal sealed class MinecraftService(HttpClient client)
{
    public async Task<(string Name, string ID, CroppedBitmap Image)> GetProfileAsync(string token)
    {
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        await using var stream = await client.GetStreamAsync("https://api.minecraftservices.com/minecraft/profile");

        var node = await JsonNode.ParseAsync(stream);

        string? name = node?["name"]?.GetValue<string>();
        string? id = node?["id"]?.GetValue<string>();
        string? skin = node?["skins"]?.AsArray()[0]?["url"]?.GetValue<string>();

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(skin);

        var image = await client.GetStreamAsync(skin);
        var memory = new MemoryStream();
        await image.CopyToAsync(memory);
        memory.Position = 0;
        var bitmap = new CroppedBitmap(new Bitmap(memory), new PixelRect(8, 8, 8, 8));
        return (name, id, bitmap);
    }

    public async Task<string> GetTokenAsync(string token, string hash)
    {
        var request = new { identityToken = $"XBL3.0 x={hash};{token}" };
        var response = await client.PostAsync("https://api.minecraftservices.com/authentication/login_with_xbox", request);

        response.EnsureSuccessStatusCode();

        return await response.Content.GetValueAsync("access_token");
    }

    public async Task DownloadAsync()
    {
        await using var stream = await client.GetStreamAsync("https://launcher.mojang.com/v1/objects/43db9b498cb67058d2e12d394e6507722e71bb45/client.jar");
        await using var file = File.OpenWrite("b1.7.3.jar");

        await stream.CopyToAsync(file);
    }
}
