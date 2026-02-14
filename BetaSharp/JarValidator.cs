using System.Security.Cryptography;

namespace BetaSharp;

internal static class JarValidator
{
    private const string EXPECTED_HASH = "af1fa04b8006d3ef78c7e24f8de4aa56f439a74d7f314827529062d5bab6db4c";

    public static bool ValidateJar(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hashBytes = sha256.ComputeHash(stream);
        var actualHash = Convert.ToHexStringLower(hashBytes);

        return actualHash.Equals(EXPECTED_HASH, StringComparison.OrdinalIgnoreCase);
    }
}