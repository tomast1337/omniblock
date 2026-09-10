namespace OmniBlock.Launcher.Features.Xbox.User;

internal sealed class UserResponse
{
    public required string Token { get; init; }

    public required UserDisplayClaims DisplayClaims { get; init; }

    internal sealed class UserDisplayClaims
    {
        public required UserXui[] Xui { get; set; }

        internal sealed class UserXui
        {
            public required string Uhs { get; init; }
        }
    }
}
