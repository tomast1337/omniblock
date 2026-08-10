using System.Text.Json.Serialization;
using OmniBlock.Launcher.Features.Mojang.Entitlements;
using OmniBlock.Launcher.Features.Mojang.Profile;
using OmniBlock.Launcher.Features.Mojang.Token;

namespace OmniBlock.Launcher.Features.Mojang;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TokenRequest))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(EntitlementsResponse))]
[JsonSerializable(typeof(EntitlementsResponse.Item))]
[JsonSerializable(typeof(ProfileResponse))]
[JsonSerializable(typeof(ProfileResponse.Skin))]
internal sealed partial class MojangSerializerContext : JsonSerializerContext;
