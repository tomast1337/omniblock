using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Extensions;

namespace BetaSharp.Launcher.Features;

internal sealed class XboxService(HttpClient client)
{
    public async Task<(string Token, string Hash)> GetTokenAsync(string microsoft)
    {
        var profile = await GetProfileAsync(microsoft);
        var request = new { Properties = new { SandboxId = "RETAIL", UserTokens = new[] { profile.Token } }, RelyingParty = "rp://api.minecraftservices.com/", TokenType = "JWT" };
        var response = await client.PostAsync("https://xsts.auth.xboxlive.com/xsts/authorize", request);

        response.EnsureSuccessStatusCode();

        return (await response.Content.GetValueAsync("Token"), profile.Hash);
    }

    private async Task<(string Token, string Hash)> GetProfileAsync(string microsoft)
    {
        var request = new { Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = $"d={microsoft}" }, RelyingParty = "http://auth.xboxlive.com", TokenType = "JWT" };
        var response = await client.PostAsync("https://user.auth.xboxlive.com/user/authenticate", request);

        await using var stream = await response.Content.ReadAsStreamAsync();

        var node = await JsonNode.ParseAsync(stream);
        string? token = node?["Token"]?.GetValue<string>();

        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        string? hash = node?["DisplayClaims"]?["xui"]?[0]?["uhs"]?.GetValue<string>();

        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        return (token, hash);
    }
}
