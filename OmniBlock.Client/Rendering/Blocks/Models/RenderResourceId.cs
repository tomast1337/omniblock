namespace OmniBlock.Client.Rendering.Blocks.Models;

/// <summary>
/// Client asset identity, not a gameplay/save catalog ID. Paths are relative, slash-separated
/// resource names; validating segments here makes ZIP lookup unambiguous and traversal-free.
/// </summary>
internal sealed record RenderResourceId
{
    private RenderResourceId(string ns, string path)
    {
        Namespace = ns;
        Path = path;
    }

    public string Namespace { get; }
    public string Path { get; }
    public string ModelPath => $"assets/{Namespace}/models/{Path}.json";

    public static RenderResourceId Parse(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var colon = name.IndexOf(':');
        if (colon <= 0 || colon != name.LastIndexOf(':')) throw new FormatException($"Invalid namespaced render resource '{name}'.");
        var ns = name[..colon];
        var path = name[(colon + 1)..];
        if (ns.Length > 256 || path.Length is 0 or > 256 || !ValidSegment(ns) || !path.Split('/').All(ValidSegment))
            throw new FormatException($"Invalid render resource '{name}': use lowercase relative path segments, without empty, '.' or '..' segments.");
        return new RenderResourceId(ns, path);
    }

    private static bool ValidSegment(string part) => part.Length > 0 && part is not ("." or "..") &&
        part.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.');

    public override string ToString() => $"{Namespace}:{Path}";
}
