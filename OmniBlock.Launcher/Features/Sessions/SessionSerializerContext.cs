using System.Text.Json.Serialization;

namespace OmniBlock.Launcher.Features.Sessions;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Session))]
internal sealed partial class SessionSerializerContext : JsonSerializerContext;
