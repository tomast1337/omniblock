using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Extensions;

namespace BetaSharp.Launcher.Features;

internal sealed class XboxService(HttpClient client)
{
    public async Task<(string Token, string Hash)> GetAsync(string microsoft)
    {
        var profileRequest = new { Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = $"d={microsoft}" }, RelyingParty = "http://auth.xboxlive.com", TokenType = "JWT" };
        var profileResponse = await client.PostAsync("https://user.auth.xboxlive.com/user/authenticate", profileRequest);

        await using var stream = await profileResponse.Content.ReadAsStreamAsync();

        var node = await JsonNode.ParseAsync(stream);
        string? token = node?["Token"]?.GetValue<string>();

        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        string? hash = node?["DisplayClaims"]?["xui"]?[0]?["uhs"]?.GetValue<string>();

        ArgumentException.ThrowIfNullOrWhiteSpace(hash);

        var securityRequest = new { Properties = new { SandboxId = "RETAIL", UserTokens = new[] { token } }, RelyingParty = "rp://api.minecraftservices.com/", TokenType = "JWT" };
        var securityResponse = await client.PostAsync("https://xsts.auth.xboxlive.com/xsts/authorize", securityRequest);

        securityResponse.EnsureSuccessStatusCode();

        return (await securityResponse.Content.GetValueAsync("Token"), hash);
    }
}
