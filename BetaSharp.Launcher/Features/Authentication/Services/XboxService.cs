using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BetaSharp.Launcher.Extensions;

namespace BetaSharp.Launcher.Features.Authentication.Services;

internal sealed class XboxService(IHttpClientFactory httpClientFactory)
{
    public sealed class Profile(string token, string hash)
    {
        public string Token => token;

        public string Hash => hash;
    }

    public async Task<Profile> GetProfileAsync(string accessToken)
    {
        var client = httpClientFactory.CreateClient();

        var request = new { Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = $"d={accessToken}" }, RelyingParty = "http://auth.xboxlive.com", TokenType = "JWT" };
        var response = await client.PostAsync("https://user.auth.xboxlive.com/user/authenticate", request);

        await using var stream = await response.Content.ReadAsStreamAsync();

        var node = await JsonNode.ParseAsync(stream);
        string? token = node?["Token"]?.GetValue<string>();

        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        string? hash = node?["DisplayClaims"]?["xui"]?[0]?["uhs"]?.GetValue<string>();

        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        var profile = new Profile(token, hash);

        return profile;
    }

    public async Task<string> GetTokenAsync(string token)
    {
        var client = httpClientFactory.CreateClient();

        var request = new { Properties = new { SandboxId = "RETAIL", UserTokens = new[] { token } }, RelyingParty = "rp://api.minecraftservices.com/", TokenType = "JWT" };
        var response = await client.PostAsync("https://xsts.auth.xboxlive.com/xsts/authorize", request);

        response.EnsureSuccessStatusCode();

        return await response.Content.GetValueAsync("Token");
    }
}
