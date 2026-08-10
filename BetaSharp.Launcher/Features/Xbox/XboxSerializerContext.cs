using System.Text.Json.Serialization;
using OmniBlock.Launcher.Features.Xbox.Token;
using OmniBlock.Launcher.Features.Xbox.User;

namespace OmniBlock.Launcher.Features.Xbox;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(UserRequest))]
[JsonSerializable(typeof(UserRequest.UserProperties))]
[JsonSerializable(typeof(UserResponse))]
[JsonSerializable(typeof(UserResponse.UserDisplayClaims))]
[JsonSerializable(typeof(UserResponse.UserDisplayClaims.UserXui))]
[JsonSerializable(typeof(TokenRequest))]
[JsonSerializable(typeof(TokenRequest.TokenProperties))]
[JsonSerializable(typeof(TokenResponse))]
internal sealed partial class XboxSerializerContext : JsonSerializerContext;
