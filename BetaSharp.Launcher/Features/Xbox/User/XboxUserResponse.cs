using System.Text.Json.Serialization;

namespace BetaSharp.Launcher.Features.Xbox.User;

internal sealed class XboxUserResponse
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
