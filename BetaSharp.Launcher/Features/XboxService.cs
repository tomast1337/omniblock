using System;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Extensions;

namespace BetaSharp.Launcher.Features;

internal sealed class XboxService(HttpClient client)
{
    public async Task<(string Token, string Hash)> GetAsync(string microsoft)
    {
        var userResponse = await client.PostAsync<UserRequest, UserResponse>(
            "https://user.auth.xboxlive.com/user/authenticate",
            new UserRequest { Properties = new UserRequest.UserProperties { RpsTicket = $"d={microsoft}" } });

        var tokenResponse = await client.PostAsync<TokenRequest, TokenResponse>(
            "https://xsts.auth.xboxlive.com/xsts/authorize",
            new TokenRequest { Properties = new TokenRequest.TokenProperties { UserTokens = [userResponse.Token] } });

        ArgumentNullException.ThrowIfNull(tokenResponse);

        return (tokenResponse.Token, userResponse.DisplayClaims.Xui[0].Uhs);
    }
}

file sealed class UserRequest
{
    public sealed class UserProperties
    {
        public string AuthMethod => "RPS";

        public string SiteName => "user.auth.xboxlive.com";

        public required string RpsTicket { get; init; }
    }

    public required UserProperties Properties { get; init; }

    public string RelyingParty => "http://auth.xboxlive.com";

    public string TokenType => "JWT";
}

file sealed class UserResponse
{
    public sealed class UserDisplayClaims
    {
        public sealed class UserXui
        {
            [JsonPropertyName("uhs")]
            public required string Uhs { get; init; }
        }

        [JsonPropertyName("xui")]
        public required UserXui[] Xui { get; set; }
    }

    public required string Token { get; init; }

    public required UserDisplayClaims DisplayClaims { get; init; }
}

file sealed class TokenRequest
{
    public sealed class TokenProperties
    {
        public string SandboxId => "RETAIL";

        public required string[] UserTokens { get; init; }
    }

    public required TokenProperties Properties { get; init; }

    public string RelyingParty => "rp://api.minecraftservices.com/";

    public string TokenType => "JWT";
}

file sealed class TokenResponse
{
    public required string Token { get; init; }
}
