using System.Text.Json.Serialization;

namespace OmniBlock.Launcher.Features.Mojang.Entitlements;

internal sealed class EntitlementsResponse
{
    [JsonPropertyName("items")] public required Item[] Items { get; init; }

    internal sealed class Item
    {
        public required string Name { get; init; }

        public required string Signature { get; init; }
    }
}
