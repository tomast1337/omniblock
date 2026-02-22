namespace BetaSharp.Launcher.Features.Xbox.Profile;

internal sealed class ProfileRequest
{
    public sealed class ProfileProperties
    {
        public string AuthMethod => "RPS";

        public string SiteName => "user.auth.xboxlive.com";

        public required string RpsTicket { get; init; }
    }

    public required ProfileProperties Properties { get; init; }

    public string RelyingParty => "http://auth.xboxlive.com";

    public string TokenType => "JWT";
}
