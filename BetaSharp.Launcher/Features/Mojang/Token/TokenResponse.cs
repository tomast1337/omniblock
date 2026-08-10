using System.Text.Json.Serialization;

namespace OmniBlock.Launcher.Features.Mojang.Token;

internal sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public required string Value { get; init; }

    [JsonPropertyName("expires_in")]
    public required int Expiration { get; init; }
}
