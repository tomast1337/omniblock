using System.Text.RegularExpressions;

namespace OmniBlock.Worlds.Storage;

public static partial class DataFilenameFilter
{
    [GeneratedRegex(@"^c\.(-?[0-9a-z]+)\.(-?[0-9a-z]+)\.dat$", RegexOptions.IgnoreCase)]
    public static partial Regex ChunkFilePattern();
}
