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
    public async Task<XboxUserResponse> GetUserAsync(string microsoftToken)
    {
        var client = clientFactory.CreateClient(nameof(XboxClient));

        var response = await client.PostAsync(
            "https://user.auth.xboxlive.com/user/authenticate",
            JsonContent.Create(
                new XboxUserRequest { Properties = new XboxUserRequest.UserProperties { RpsTicket = $"d={microsoftToken}" } },
                SourceGenerationContext.Default.XboxUserRequest));

        await using var stream = await response.Content.ReadAsStreamAsync();

        var instance = JsonSerializer.Deserialize<XboxUserResponse>(stream, SourceGenerationContext.Default.XboxUserResponse);

        ArgumentNullException.ThrowIfNull(instance);

        return instance;
    }

    public async Task<XboxTokenResponse> GetTokenAsync(string userToken)
    {
        var client = clientFactory.CreateClient(nameof(XboxClient));

        var response = await client.PostAsync(
            "https://xsts.auth.xboxlive.com/xsts/authorize",
            JsonContent.Create(
                new XboxTokenRequest { Properties = new XboxTokenRequest.TokenProperties { UserTokens = [userToken] } },
                SourceGenerationContext.Default.XboxTokenRequest));

        await using var stream = await response.Content.ReadAsStreamAsync();

        var instance = JsonSerializer.Deserialize<XboxTokenResponse>(stream, SourceGenerationContext.Default.XboxTokenResponse);

        ArgumentNullException.ThrowIfNull(instance);

        return instance;
    }
}
