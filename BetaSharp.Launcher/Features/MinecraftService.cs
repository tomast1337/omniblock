using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Extensions;

namespace BetaSharp.Launcher.Features;

internal sealed class MinecraftService(HttpClient client)
{
    public async Task<string> GetTokenAsync(string token, string hash)
    {
        var response = await client.PostAsync<TokenRequest, TokenResponse>(
            "https://api.minecraftservices.com/authentication/login_with_xbox",
            new TokenRequest { IdentityToken = $"XBL3.0 x={hash};{token}" });

        ArgumentNullException.ThrowIfNull(response);

        return response.AccessToken;
    }

    public async Task<(string Name, string? Skin)> GetProfileAsync(string token)
    {
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

        var response = await client.GetFromJsonAsync<ProfileResponse>("https://api.minecraftservices.com/minecraft/profile");

        ArgumentNullException.ThrowIfNull(response);

        return (response.Name, response.Skins.FirstOrDefault(skin => skin.State.Equals("active", StringComparison.InvariantCultureIgnoreCase))?.Url);
    }
}

file sealed class TokenRequest
{
    [JsonPropertyName("identityToken")]
    public required string IdentityToken { get; init; }
}

file sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }
}

file sealed class ProfileResponse
{
    public sealed class Skin
    {
        [JsonPropertyName("url")]
        public required string Url { get; init; }

        [JsonPropertyName("state")]
        public required string State { get; init; }
    }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("skins")]
    public required Skin[] Skins { get; init; }
}
