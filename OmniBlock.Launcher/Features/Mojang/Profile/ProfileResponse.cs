namespace OmniBlock.Launcher.Features.Mojang.Profile;

internal sealed class ProfileResponse
{
    public required string Name { get; init; }

    public required Skin[] Skins { get; init; }

    internal sealed class Skin
    {
        public required string Url { get; init; }

        public required string State { get; init; }
    }
}
