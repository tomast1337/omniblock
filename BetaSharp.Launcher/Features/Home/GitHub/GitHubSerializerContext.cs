using System.Text.Json.Serialization;

namespace OmniBlock.Launcher.Features.Home.GitHub;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ReleasesResponse[]))]
internal sealed partial class GitHubSerializerContext : JsonSerializerContext;
