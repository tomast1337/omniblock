using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using BetaSharp.Launcher.Features.Xbox.Token;
using BetaSharp.Launcher.Features.Xbox.User;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Launcher.Features.Xbox;

internal sealed class XboxClient(ILogger<XboxClient> logger, IHttpClientFactory clientFactory)
{
    public async Task<UserResponse> GetProfileAsync(string microsoftToken)
    {
        var client = clientFactory.CreateClient(nameof(XboxClient));

        var response = await client.PostAsync(
            "https://user.auth.xboxlive.com/user/authenticate",
            JsonContent.Create(
                new UserRequest { Properties = new UserRequest.UserProperties { RpsTicket = $"d={microsoftToken}" } },
                XboxSerializerContext.Default.UserRequest));

        await using var stream = await response.Content.ReadAsStreamAsync();

        var instance = JsonSerializer.Deserialize<UserResponse>(stream, XboxSerializerContext.Default.UserResponse);

        ArgumentNullException.ThrowIfNull(instance);

        return instance;
    }

    public async Task<TokenResponse> GetTokenAsync(string userToken)
    {
        var client = clientFactory.CreateClient(nameof(XboxClient));

        var response = await client.PostAsync(
            "https://xsts.auth.xboxlive.com/xsts/authorize",
            JsonContent.Create(
                new TokenRequest { Properties = new TokenRequest.TokenProperties { UserTokens = [userToken] } },
                XboxSerializerContext.Default.TokenRequest));

        await using var stream = await response.Content.ReadAsStreamAsync();

        var instance = JsonSerializer.Deserialize<TokenResponse>(stream, XboxSerializerContext.Default.TokenResponse);

        ArgumentNullException.ThrowIfNull(instance);

        return instance;
    }
}
