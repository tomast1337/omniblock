using System;

namespace BetaSharp.Launcher.Features.Sessions;

internal sealed class Session
{
    public required string Name { get; init; }

    public required string Skin { get; init; }

    public required string Token { get; set; }

    public required DateTimeOffset Expiration { get; set; }
}
