using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BetaSharp.Launcher.Features.New.Authentication.Extensions;

internal static class HttpContentExtensions
{
    public static async Task<string> GetValueAsync(this HttpContent content, string key)
    {
        await using var stream = await content.ReadAsStreamAsync();

        var node = await JsonNode.ParseAsync(stream);
        string? value = node?[key]?.GetValue<string>();

        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return value;
    }
}