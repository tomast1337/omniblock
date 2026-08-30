using System.Text;

namespace OmniBlock.Client;

/// <summary>Command-line configuration resolved before the client is constructed.</summary>
internal sealed record ClientLaunchOptions(
    string Username,
    string SessionToken,
    bool Debug,
    StartupScript? StartupScript)
{
    public static ClientLaunchOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? username = null;
        string? token = null;
        bool debug = false;
        string? startupScriptPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--username":
                    username = ReadValue(args, ref i, "--username");
                    break;
                case "--token":
                    token = ReadValue(args, ref i, "--token");
                    break;
                case "--startup-script":
                    startupScriptPath = ReadValue(args, ref i, "--startup-script");
                    break;
                case "--debug":
                    debug = true;
                    break;
            }
        }

        username ??= $"Player{Random.Shared.Next()}";
        token ??= "-";
        PlayerNameValidator.Validate(username);

        StartupScript? startupScript = startupScriptPath == null
            ? null
            : LoadStartupScript(startupScriptPath);

        return new ClientLaunchOptions(username, token, debug, startupScript);
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} requires a value.", nameof(args));
        }

        return args[++index];
    }

    private static StartupScript LoadStartupScript(string path)
    {
        string fullPath = Path.GetFullPath(path);

        try
        {
            return new StartupScript(fullPath, File.ReadAllText(fullPath, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ArgumentException($"Could not load startup script '{fullPath}': {ex.Message}", nameof(path), ex);
        }
    }
}

internal sealed record StartupScript(string Path, string Source);
