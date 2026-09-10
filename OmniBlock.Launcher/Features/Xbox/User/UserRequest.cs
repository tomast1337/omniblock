namespace OmniBlock.Launcher.Features.Xbox.User;

internal sealed class UserRequest
{
    public required UserProperties Properties { get; init; }

    public string RelyingParty => "http://auth.xboxlive.com";

    public string TokenType => "JWT";

    internal sealed class UserProperties
    {
        public string AuthMethod => "RPS";

        public string SiteName => "user.auth.xboxlive.com";

        public required string RpsTicket { get; init; }
    }
}
