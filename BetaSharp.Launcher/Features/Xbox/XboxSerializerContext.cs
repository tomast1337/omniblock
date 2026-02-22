using System.Text.Json.Serialization;
using BetaSharp.Launcher.Features.Xbox.Profile;
using BetaSharp.Launcher.Features.Xbox.Token;

namespace BetaSharp.Launcher.Features.Xbox;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ProfileRequest))]
[JsonSerializable(typeof(ProfileRequest.ProfileProperties))]
[JsonSerializable(typeof(UserResponse))]
[JsonSerializable(typeof(UserResponse.UserDisplayClaims))]
[JsonSerializable(typeof(UserResponse.UserDisplayClaims.UserXui))]
[JsonSerializable(typeof(TokenRequest))]
[JsonSerializable(typeof(TokenRequest.TokenProperties))]
[JsonSerializable(typeof(TokenResponse))]
internal sealed partial class XboxSerializerContext : JsonSerializerContext;
